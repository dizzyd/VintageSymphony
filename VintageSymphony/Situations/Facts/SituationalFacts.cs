namespace VintageSymphony.Situations.Facts;

public struct SituationalFacts
{
	public SituationalFacts()
	{
	}

	public float DistanceTravelledTotal;
	public float DistanceTravelledDiagonal;
	public float MovementRadius;
	public float SecondsSinceLastDamage = float.PositiveInfinity;
	public float SecondsSinceLastAttack = float.PositiveInfinity;
	public float DistanceFromHome;
	public float Time;
	public long Now;
	public float RelativeHeight;
	public float DistanceToSurface;
	public bool IsHoldingWeapon;
	public float EnemyDistance = float.PositiveInfinity;
	public float VisibleEnemyDistance = float.PositiveInfinity;
	public const float EnemyDistanceMax = 50f;
	public float RiftDistance = float.PositiveInfinity;
	public float PlayingResonatorDistance = float.PositiveInfinity;
	public const int PlayingResonatorDistanceMax = 18;
	public float SunLevel;
	public float DayLight;
	public bool Alive;

	/// <summary>
	/// How many ways out the game's room registry found from where the player stands.
	/// Zero is a sealed room - the same test that makes a cellar a cellar. Unknown until
	/// the first scan.
	/// </summary>
	public int RoomExitCount = -1;

	public bool IsInEnclosedRoom => RoomExitCount == 0;
}