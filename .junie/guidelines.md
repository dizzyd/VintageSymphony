Vintage Symphony — Project Guidelines for Advanced Contributors

Scope
- This document captures project-specific build, configuration, packaging, and development conventions used across the Vintage Symphony repository. It assumes familiarity with .NET SDK, Cake Frosting, and the Vintage Story modding environment.

Build and Configuration
1. Toolchain
   - .NET SDK: The solution targets net10.0 for both VintageSymphony and CakeBuild projects, which is what Vintage Story 1.22 runs on. A global.json is present with sdk.version=7.0.0 and rollForward=latestMajor, so any newer SDK on the machine is used; in practice that means SDK 10.0.x.
   - Game version: 1.22 and newer. modinfo.json declares `"game": "1.22.0"` - a bare version string, which the game reads as a minimum. A net10.0 build cannot load on 1.21 or earlier, so that constraint is what keeps it off those versions.
   - OS: Build tested primarily on Linux and Windows; macOS should work if prerequisites are satisfied.

2. External Dependencies (mandatory)
   - Vintage Story game libraries are not NuGet packages and must be provided via an environment variable:
     - VINTAGE_STORY must point to the Vintage Story installation folder containing:
       - VintagestoryAPI.dll
       - VintagestoryLib.dll
       - Mods/VSSurvivalMod.dll
       - Lib/0Harmony.dll
       - Lib/cairo-sharp.dll
       - Lib/Newtonsoft.Json.dll
     - Example values:
       - Windows (PowerShell): $Env:VINTAGE_STORY = "C:\Program Files\Vintagestory"
       - Linux (bash): export VINTAGE_STORY="$HOME/.vintagestory"
   - Without VINTAGE_STORY defined, dotnet build/publish will fail for both the main project and CakeBuild.

3. Solution and Projects
   - Solution file: MM.sln
   - Projects:
     - VintageSymphony/VintageSymphony.csproj (net10.0)
       - Nullable: enable; ImplicitUsings: enable
       - Copies VintageSymphony/modinfo.json and everything under VintageSymphony/assets/** (if present) to output
       - References external DLLs via $(VINTAGE_STORY)
     - CakeBuild/CakeBuild.csproj (net10.0)
       - Cake.Frosting-based build/packaging pipeline
       - References VintagestoryAPI via $(VINTAGE_STORY)

4. Building
   - Fast path (no packaging):
     - dotnet build MM.sln -c Release
     - Requires VINTAGE_STORY to be set.
   - Full pipeline + packaging (reproducible releases):
     - Linux: ./build.sh [--configuration Debug|Release] [--skipJsonValidation=true]
     - Windows: ./build.ps1 [-configuration Debug|Release] [-skipJsonValidation true]
     - Defaults: configuration=Release, skipJsonValidation=false
   - JSON validation:
     - ValidateJson task parses all JSON under VintageSymphony/assets/**/*.json. If you intentionally work without an assets tree, call the pipeline with --skipJsonValidation=true or create a minimal assets folder to satisfy validation.

5. Outputs and Packaging
   - CakeBuild orchestrates:
     - Build: dotnet clean + dotnet publish VintageSymphony
     - PackageMod: creates zip archives under Releases/
       - Mod ZIP: {modid}_{version}.zip from VintageSymphony/modinfo.json
       - Assets ZIP: {assetsModId}_{version}.zip from Assets/modinfo.json
       - ZIP contents:
         - Code ZIP: VintageSymphony.dll (+ .pdb for Debug), modinfo.json
         - Assets ZIP: Assets/music/*.ogg, musicconfig.json, musicconfig-readme.txt, Assets/modinfo.json, attributions.txt
   - Release dir is cleaned each run; anything under Releases/ is considered disposable build output.

6. Running in Vintage Story
   - Option 1 (ZIP install): Copy the generated ZIP(s) from Releases/ to the game’s Mods directory.
   - Option 2 (loose files): Copy VintageSymphony.dll and modinfo.json into a folder named vintagesymphony under Mods; likewise, put Assets ZIP or unpacked assets under vintagesymphonyassets.
   - Version coupling: Assets/modinfo.json depends on vintagesymphony "+1.0.0"; ensure code version (VintageSymphony/modinfo.json) and assets version remain compatible.

Development Conventions
1. Code Style
   - Braces: Allman style
     - Opening brace on a new line for namespaces, types, methods, properties, and control blocks.
       Example:
       public class Foo
       {
           public void Bar()
           {
               if (cond)
               {
                   // ...
               }
           }
       }
   - Indentation: Tabs are used throughout current sources; maintain existing indentation in touched files.
   - Naming:
     - Public types/members: PascalCase (Playback, NextTrack, CurrentTrack)
     - Private fields: camelCase (logger, trackCooldownManager)
     - Constants: PascalCase
     - Local vars: use var where the type is apparent
   - Nullability: <Nullable>enable</Nullable> in csproj. Prefer explicit null checks and nullable annotations. Avoid null-forgiving unless justified.
   - Early returns are preferred for guard clauses to reduce nesting.
   - LINQ usage: Favor readable, allocation-conscious pipelines; call out hotspots and consider imperative alternatives if in hot paths.
   - Randomness: Uses System.Random.Shared; keep deterministic boundaries where applicable.

2. Project Patterns and Architecture Notes
   - Game Integration:
     - Main engine integrates with Vintage Story via VintagestoryAPI types (ICoreClientAPI, ILogger, etc.). UI overlays use Cairo and VS GUI composers.
   - Playback Engine:
     - Playback coordinates tracks, cooldowns (TrackCooldownManager), restrictions (TrackRestrictionMatcher), and pause logic.
     - Frequency setting influences pause durations and cooldown length; see Playback.SetMusicFrequency and GetPauseDuration.
     - Track selection weights by Priority, then randomizes within tier.
   - Situations System:
     - Situations contribute attributes (e.g., DynamicSituation, PauseAfterPlayback) that directly affect playback flow. When changing situation logic, verify interactions with Pause and NextTrack/StopTrackAndPause.
   - Packaging:
     - The build uses the ModInfo from VintageSymphony/modinfo.json and Assets/modinfo.json to derive output names; change versions there for releases. Keep these in sync when pushing a release tag.

3. Debugging and Diagnostics
   - Logging: ILogger from Vintagestory.API.Common is used (e.g., Notification for user-visible, Debug for internal). Be mindful of spam in tight loops (e.g., per-frame Update) — gate logs or raise log level only for critical state changes.
   - Overlays: The DebugOverlay and UpdateProgressOverlay render with Cairo; use AddDynamicCustomDraw with a stable key and call Redraw() on updates. Keep drawing code allocation-free per frame.
   - JSON issues: The ValidateJson task provides early feedback on malformed JSON in assets. When iterating on assets, run ./build.sh --skipJsonValidation=true to shorten loops, but validate before commit.

4. Asset Management
   - Audio: .ogg files live under Assets/music and are packaged into the assets ZIP only; the code project does not embed these.
   - Config: musicconfig.json and musicconfig-readme.txt accompany audio in assets packaging.
   - Attributions: Assets/attributions.txt is included in the assets ZIP and must be updated with any new track.

5. Local Development Tips
   - IDE: Rider/VS work fine. Ensure the environment variable VINTAGE_STORY is present in the IDE’s run/build environment.
   - Incremental testing in-game: For code-only changes, you can copy just the rebuilt DLL + modinfo.json into the Mods/vintagesymphony folder without repackaging assets.
   - JSON-free builds: If you don’t have VintageSymphony/assets in your working copy, either create a stub tree or pass --skipJsonValidation=true.

6. Versioning and Releases
   - Bump versions in both modinfo.json files before packaging. The assets mod declares a dependency on vintagesymphony; ensure minimum version alignment.
   - The Cake pipeline zips to Releases/; these are the artifacts to upload to ModDB or distribute.

7. CI/CD (if introduced later)
   - Mirror the build.sh invocation; set VINTAGE_STORY in CI to a path containing the required DLLs (commonly a cached artifact). Cache NuGet and Releases/ as appropriate.

Known Build Pitfalls
- Missing VINTAGE_STORY env var: build fails at reference resolution. Fix by exporting the correct path.
- JSON validation failing: Either correct the JSON under VintageSymphony/assets or pass --skipJsonValidation=true during local iteration.
- Mismatched versions between assets and code: Will cause dependency resolution issues in-game; ensure compatible versions before release.

Repository Hygiene
- Do not commit Releases/ zips or bin/obj output.
- Preserve tab indentation and Allman braces in edits.
- When adding new assets or situations, keep code/asset changes in the same PR to maintain integrity.
