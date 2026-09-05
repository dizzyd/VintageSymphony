using System.Xml.Schema;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageSymphony.Storage;
using VintageSymphony.Util;

namespace VintageSymphony.Situations.Facts;

public class SituationalFactsCollector
{
	struct PositionSample
	{
		public Vec3f Position;
		public long Time;
	}

	private static readonly string[] EnemyTypes =
		{ "drifter", "shiver", "bowtorn", "locust", "wolf", "bear", "hyena", "bell", "eidolon" };

	private readonly AttributeStorage attributeStorage;
	private readonly IDictionary<AssetLocation, bool> enemyCodeCache = new Dictionary<AssetLocation, bool>();
	private EntityPlayer PlayerEntity => clientApi.World.Player.Entity;

	private SituationalFacts facts = new();

	private readonly LinkedList<PositionSample> playerPositionSamples = new(); // distances travelled per second
	private const float PlayerPositionFrameDuration = 60f;

	private readonly EnumTool[] weapons =
	{
		EnumTool.Bow,
		EnumTool.Sling,
		EnumTool.Spear,
		EnumTool.Sword,
	};

	private ICoreClientAPI clientApi;
	private long timeLastDamageTaken = -1L;
	private long timeLastAttackDetected = -1L;
	private int lastHurtCounter;

	private const int
		AttackCooldownSeconds = 2; // Time in seconds that isAttacked remains true after enemies stop attacking

	private readonly int worldHeight;
	private readonly int seaLevel;
	private readonly ModSystemRifts riftModSystem;

	/// <summary>Past this a visible enemy scores the same as none at all - see DangerEvaluator.</summary>
	private const float VisibleEnemyRelevantDistance = 25f;

	private const int ResonatorScanIntervalMs = 1000;
	private readonly long resonatorScanListenerId;
	private volatile float playingResonatorDistance = float.PositiveInfinity;

	public SituationalFactsCollector(AttributeStorage attributeStorage)
	{
		clientApi = VintageSymphony.ClientApi;
		this.attributeStorage = attributeStorage;
		lastHurtCounter = PlayerEntity.WatchedAttributes.GetInt("onHurtCounter");
		PlayerEntity.WatchedAttributes.RegisterModifiedListener("onHurt", OnPlayerHurt);
		clientApi.Event.BlockChanged += OnBlockChanged;
		worldHeight = clientApi.World.BlockAccessor.MapSize.Y;
		seaLevel = clientApi.World.SeaLevel;
		riftModSystem = clientApi.ModLoader.GetModSystem<ModSystemRifts>();
		resonatorScanListenerId =
			clientApi.World.RegisterGameTickListener(ScanForPlayingResonators, ResonatorScanIntervalMs);
	}

	public void Dispose()
	{
		clientApi.World.UnregisterGameTickListener(resonatorScanListenerId);
		clientApi.Event.BlockChanged -= OnBlockChanged;
	}

	private long GetNow() => clientApi.InWorldEllapsedMilliseconds;

	private void OnBlockChanged(BlockPos pos, Block oldblock)
	{
		var newBlock = clientApi.World.BlockAccessor.GetBlock(pos);
		if (IsBedBlock(newBlock) && newBlock.Code.Path.Contains("-feet-"))
		{
			attributeStorage.PlayerHomeLocations.Add(pos.AsVec3i);
		}
		else if (IsBedBlock(oldblock) && newBlock.Code.PathStartsWith("air"))
		{
			attributeStorage.PlayerHomeLocations.RemoveAll(v => v.SquareDistanceTo(pos.AsVec3i) <= 1);
		}
	}

	/// <summary>
	/// The onHurt attribute is modified whenever the player's attributes are synced whole
	/// - on joining, on teleporting - not only when they are hurt, so the game's own
	/// health behaviour checks that the hit counter moved before believing it. Without
	/// the same check every teleport read as a wound, and Danger and Fight carried it for
	/// the next minute.
	/// </summary>
	private void OnPlayerHurt()
	{
		var attributes = PlayerEntity.WatchedAttributes;
		int counter = attributes.GetInt("onHurtCounter");
		if (attributes.GetFloat("onHurt") == 0f || counter == lastHurtCounter)
		{
			return;
		}

		lastHurtCounter = counter;
		timeLastDamageTaken = GetNow();
		facts.SecondsSinceLastDamage = 0;
	}

	public SituationalFacts GatherFacts(float dt)
	{
		UpdateMovementDistances();
		UpdateMovementRadius();
		UpdateDistanceFromHome();
		UpdateTime();
		UpdateHeight();
		UpdateHoldingWeapon();

		var nearbyEnemies = FetchNearbyEnemies();
		UpdateEnemyDistance(nearbyEnemies);
		UpdateIsAttacked(nearbyEnemies);

		UpdateRiftDistance();
		UpdateSunFacts();
		UpdateAlive();
		facts.PlayingResonatorDistance = playingResonatorDistance;

		return facts;
	}

	private void UpdateHoldingWeapon()
	{
		var item = PlayerEntity.RightHandItemSlot?.Itemstack?.Item;
		EnumTool? tool = item?.Tool;

		facts.IsHoldingWeapon = item != null
		                        && (tool.HasValue && weapons.Contains(tool.Value) ||
		                            item.Code.BeginsWith("game", "club"));
	}

	private void UpdateMovementDistances()
	{
		long now = GetNow();

		var sample = new PositionSample
		{
			Position = PlayerEntity.Pos.XYZFloat,
			Time = now
		};
		playerPositionSamples.AddLast(sample);
		long timeLimit = now - (long)(PlayerPositionFrameDuration * 1000);
		while (playerPositionSamples.Count > 0 && playerPositionSamples.First!.Value.Time < timeLimit)
		{
			playerPositionSamples.RemoveFirst();
		}

		facts.DistanceTravelledTotal = 0;
		facts.DistanceTravelledDiagonal = 0;
		if (playerPositionSamples.Count < 2)
		{
			return;
		}

		var previousNode = playerPositionSamples.First;
		for (var currentNode = previousNode!.Next;
		     currentNode!.Next != null;
		     previousNode = currentNode, currentNode = currentNode.Next)
		{
			facts.DistanceTravelledTotal += previousNode.Value.Position.DistanceTo(currentNode.Value.Position);
		}

		facts.DistanceTravelledDiagonal =
			playerPositionSamples.First!.Value.Position.DistanceTo(playerPositionSamples.Last!.Value.Position);
	}

	private void UpdateMovementRadius()
	{
		var boundingSphere = BoundingSphere.Calculate(playerPositionSamples.Select(s => s.Position));
		facts.MovementRadius = boundingSphere.Radius;
	}

	private void UpdateDistanceFromHome()
	{
		var playerPosition = PlayerEntity.Pos.XYZInt!;
		var homeLocations = attributeStorage.PlayerHomeLocations;

		if (homeLocations.Count == 0)
		{
			facts.DistanceFromHome = float.PositiveInfinity;
			return;
		}

		var nearestDistanceSq = long.MaxValue;
		for (int i = 0; i < homeLocations.Count; i++)
		{
			nearestDistanceSq = long.Min(nearestDistanceSq, playerPosition.SquareDistanceTo(homeLocations[i]));
		}

		facts.DistanceFromHome = MathF.Sqrt(nearestDistanceSq);
	}

	private void UpdateTime()
	{
		var calendar = clientApi.World.Calendar;
		facts.Time = calendar.HourOfDay / calendar.HoursPerDay;
		facts.Now = GetNow();
		facts.SecondsSinceLastDamage = timeLastDamageTaken > 0
			? (float)(facts.Now - timeLastDamageTaken) / 1000L
			: float.PositiveInfinity;
	}

	private void UpdateHeight()
	{
		var playerPosition = PlayerEntity.Pos;
		var playerHeight = (float)playerPosition.Y;
		var terrainHeight = clientApi.World.BlockAccessor.GetTerrainMapheightAt(playerPosition.AsBlockPos);
		facts.RelativeHeight = MoreMath.Normalize(playerHeight, 0, seaLevel, worldHeight);
		facts.DistanceToSurface = terrainHeight - playerHeight;
	}

	private Entity[] FetchNearbyEnemies()
	{
		const float maxHorizontalDistance = SituationalFacts.EnemyDistanceMax;
		const float maxVerticalDistance = 15;

		return clientApi.World.GetEntitiesAround(
			PlayerEntity.Pos.XYZ,
			maxHorizontalDistance,
			maxVerticalDistance,
			IsEntityEnemy
		);
	}

	/// <summary>
	/// Distance first, line of sight second. Only the *nearest* visible enemy is wanted,
	/// so ordering by distance and stopping at the first one in sight costs one raytrace
	/// where testing every enemy cost one each - and raytracing is the second most
	/// expensive thing here, growing with the size of the horde.
	/// </summary>
	private void UpdateEnemyDistance(Entity[] nearbyEnemies)
	{
		var playerPos = PlayerEntity.Pos.XYZFloat;

		float closestDistance = float.PositiveInfinity;
		var distances = new float[nearbyEnemies.Length];

		for (int i = 0; i < nearbyEnemies.Length; i++)
		{
			distances[i] = MoreMath.DistanceWithWeightedVerticality(nearbyEnemies[i].Pos.XYZFloat, playerPos, 3f);
			if (distances[i] < closestDistance)
			{
				closestDistance = distances[i];
			}
		}

		// Sorts nearbyEnemies alongside, which UpdateIsAttacked does not mind - it reads
		// all of them regardless of order.
		Array.Sort(distances, nearbyEnemies);

		float closestVisibleDistance = float.PositiveInfinity;
		for (int i = 0; i < nearbyEnemies.Length; i++)
		{
			// Nothing further out can contribute: DangerEvaluator maps a visible enemy at
			// 25 blocks to zero and FightEvaluator gives up at 10, so raytracing to the
			// 50 block fetch radius can only produce a value indistinguishable from
			// nothing in sight. Sorted by distance, so the rest are further still.
			if (distances[i] > VisibleEnemyRelevantDistance)
			{
				break;
			}

			if (IsEntityVisible(nearbyEnemies[i]))
			{
				closestVisibleDistance = distances[i];
				break;
			}
		}

		facts.EnemyDistance = closestDistance;
		facts.VisibleEnemyDistance = closestVisibleDistance;
	}

	private bool IsEntityVisible(Entity entity)
	{
		try
		{
			var playerEyePos = PlayerEntity.Pos.XYZ + PlayerEntity.LocalEyePos;

			// Aim at the middle of the creature, not at its feet: a ray from eye height
			// down to the ground clips the soil in front of a creature standing a couple
			// of blocks away, so an enemy in plain sight reads as hidden.
			var targetHeight = (entity.SelectionBox?.Y2 ?? entity.CollisionBox?.Y2 ?? 1f) / 2f;

			clientApi.World.RayTraceForSelection(
				playerEyePos,
				entity.Pos.XYZ.Add(0, targetHeight, 0),
				ref raytraceIntersectionBlock,
				ref raytraceIntersectionEntity,
				efilter: _ => false);

			switch (raytraceIntersectionBlock?.Block?.BlockMaterial)
			{
				case EnumBlockMaterial.Stone:
				case EnumBlockMaterial.Ore:
				case EnumBlockMaterial.Metal:
				case EnumBlockMaterial.Mantle:
				case EnumBlockMaterial.Brick:
				case EnumBlockMaterial.Ceramic:
				case EnumBlockMaterial.Wood:
				case EnumBlockMaterial.Soil:
				case EnumBlockMaterial.Gravel:
				case EnumBlockMaterial.Sand:
				case EnumBlockMaterial.Snow:
				case EnumBlockMaterial.Ice:
					return false;
				default:
					return true;
			}
		}
		catch (IndexOutOfRangeException)
		{
			// ignore
			// workaround for https://github.com/anegostudios/VintageStory-Issues/issues/5126
			return false;
		}
	}

	private void UpdateIsAttacked(Entity[] nearbyEnemies)
	{
		bool isCurrentlyAttacked = false;
		var playerPos = PlayerEntity.Pos.XYZ;

		// Use the already fetched nearby entities
		foreach (var entity in nearbyEnemies)
		{
			// Check if entity is attacking (animation check)
			var attackAnimation = entity.AnimManager?.IsAnimationActive("attack") ?? false;
			var hurtAnimation = entity.AnimManager?.IsAnimationActive("hurt") ?? false;
			var runAnimation = entity.AnimManager?.IsAnimationActive("run") ?? false;

			// Check if entity is moving toward player
			var entityToPlayerVec = playerPos.SubCopy(entity.Pos.XYZ);
			entityToPlayerVec.Y = 0; // Ignore height difference for direction check
			entityToPlayerVec = entityToPlayerVec.Normalize();

			var entityMovementVec = entity.ServerPos.Motion.Clone();
			entityMovementVec.Y = 0; // Ignore vertical motion

			var movingTowardPlayer = false;
			if (entityMovementVec.Length() > 0.01) // Entity is moving
			{
				entityMovementVec = entityMovementVec.Normalize();
				var dot = entityMovementVec.Dot(entityToPlayerVec);
				movingTowardPlayer = dot > 0.3; // Entity is generally moving toward player
			}

			// Entity is considered attacking if:
			// 1. It's in attack animation, OR
			// 2. It's running toward the player (walk animations excluded per request)
			if (attackAnimation || hurtAnimation || (movingTowardPlayer && runAnimation))
			{
				isCurrentlyAttacked = true;
				break;
			}
		}

		long now = GetNow();
		if (isCurrentlyAttacked)
		{
			timeLastAttackDetected = now;
		}

		// Never attacked is not "attacked when the world started": measured from the -1
		// sentinel, the seconds since the last attack were the seconds since joining, and
		// Fight's attack term read as a full-blown attack for the first ten of them.
		facts.SecondsSinceLastAttack = timeLastAttackDetected >= 0
			? (float)(now - timeLastAttackDetected) / 1000L
			: float.PositiveInfinity;
	}

	private void UpdateRiftDistance()
	{
		Vec3d playerPos = PlayerEntity.Pos.XYZ;
		float sqrDistance = riftModSystem.nearestRifts?
			.Select(r => (float?)playerPos.SquareDistanceTo(r.Position))
			.DefaultIfEmpty()
			.Min() ?? float.PositiveInfinity;
		facts.RiftDistance = MathF.Sqrt(sqrDistance);
	}

	private void UpdateSunFacts()
	{
		facts.SunLevel = VintageSymphony.ClientMain.playerProperties.sunSlight;
		facts.DayLight = VintageSymphony.ClientMain.playerProperties.DayLight;
	}

	private void UpdateAlive()
	{
		facts.Alive = VintageSymphony.ClientMain.EntityPlayer.Alive;
	}

	/// <summary>
	/// A resonator is a block entity, and the chunks already index those - so ask the
	/// chunks rather than reading every block in range. Walking the 37^3 blocks the old
	/// way cost more than everything else this collector does put together, and it cost
	/// it whether or not a resonator existed.
	///
	/// Block entity dictionaries belong to the main thread, so this runs there on its own
	/// tick and leaves the answer for the fact thread to pick up. Once a second is ample
	/// for "is a resonator playing near me".
	/// </summary>
	private void ScanForPlayingResonators(float dt)
	{
		const int radius = SituationalFacts.PlayingResonatorDistanceMax;
		const int chunkSize = GlobalConstants.ChunkSize;

		var playerPos = PlayerEntity.Pos.AsBlockPos;
		var nearest = float.PositiveInfinity;

		int minChunkY = Math.Max(0, (playerPos.Y - radius) / chunkSize);
		int maxChunkY = (playerPos.Y + radius) / chunkSize;

		for (int cx = (playerPos.X - radius) / chunkSize; cx <= (playerPos.X + radius) / chunkSize; cx++)
		for (int cy = minChunkY; cy <= maxChunkY; cy++)
		for (int cz = (playerPos.Z - radius) / chunkSize; cz <= (playerPos.Z + radius) / chunkSize; cz++)
		{
			var chunk = clientApi.World.BlockAccessor.GetChunk(cx, cy, cz);
			if (chunk?.BlockEntities == null)
			{
				continue;
			}

			foreach (var (blockPos, blockEntity) in chunk.BlockEntities)
			{
				if (blockEntity is not BlockEntityResonator { IsPlaying: true })
				{
					continue;
				}

				// The chunks cover the range in whole chunks, so the corners reach further
				// than the radius does.
				var distance = blockPos.DistanceTo(playerPos);
				if (distance <= radius && distance < nearest)
				{
					nearest = (float)distance;
				}
			}
		}

		playingResonatorDistance = nearest;
	}

	private static bool IsBedBlock(Block? block)
	{
		return block?.Code.PathStartsWith("bed") ?? false;
	}

	private bool IsEntityEnemy(Entity entity)
	{
		
		if (!entity.IsCreature || !entity.Alive)
		{
			return false;
		}

		if (enemyCodeCache.TryGetValue(entity.Code, out bool isEnemy))
		{
			return isEnemy;
		}

		isEnemy = false;
		for (int i = 0; i < EnemyTypes.Length; i++)
		{
			if (entity.Code.PathStartsWith(EnemyTypes[i])
			    && !entity.Code.Path.Contains("baby")
			    && !entity.Code.Path.Contains("hacked"))
			{
				isEnemy = true;
				break;
			}
		}

		enemyCodeCache[entity.Code] = isEnemy;
		return isEnemy;
	}

	static BlockSelection raytraceIntersectionBlock = new();
	static EntitySelection raytraceIntersectionEntity = new();
}