# Adding your own music to Vintage Symphony

This is for anyone who wants their own tracks in the game and is comfortable
editing a text file. There is no code to write. You make a folder, drop in some
`.ogg` files, and edit two small JSON files: one that tells the mod the folder
exists, and one that says when each track should play.

## How the mod thinks about music

Vintage Symphony watches what is happening to you in the game and boils it down
to a handful of **situations**: you are calm, you are fighting, you are deep in a
cave, a temporal storm is on, and so on. Every few seconds it scores each
situation, picks the one that fits best, and plays a track that was tagged for it.

So "configuring" music means two things:

1. **Putting tracks somewhere the mod looks.** A folder of music is called a
   *source*. The mod's own pack is one source, the game's built-in music is
   another, and yours will be a third. Each can be switched on and off in the
   config dialog (`.music config` in chat).
2. **Tagging each track with the situations it suits.** A track can suit several.
   A track tagged for nothing never plays.

## Where the files go

Everything lives in your Vintage Story data folder:

| | |
|---|---|
| Windows | `%APPDATA%\VintagestoryData` |
| Linux | `~/.config/VintagestoryData` |
| macOS | `~/.config/VintagestoryData` |

Inside it the mod keeps its sources here:

```
VintagestoryData/
└── ModData/
    └── vintagesymphonyforked/
        ├── sources.json              the list of sources
        └── sources/
            ├── vintagesymphony/      the downloaded pack
            │   └── music/
            └── my-tunes/             yours
                └── music/
                    ├── tracks.json   when each track plays
                    ├── hearth.ogg
                    └── war-drums.ogg
```

The `sources.json` file and the `sources` folder appear the first time you load a
world with the mod installed. If you do not see them yet, start a world once.

## Step by step

### 1. Pick a name

The folder name is also the source's id, and it doubles as an internal name for
the tracks, so keep it plain: **lowercase letters, digits and dashes only**, no
spaces. `my-tunes`, `dwarf-fortress`, `lofi` are all fine. `My Tunes` is not.

### 2. Make the folder and add the music

Create `sources/<your-name>/music/` and copy your `.ogg` files into it. Name the
files the same way, lowercase with dashes: `hearth.ogg`, not `Hearth (final).ogg`.

Vintage Story only plays Ogg Vorbis. If your music is MP3, FLAC or WAV, convert
it first. With [ffmpeg](https://ffmpeg.org/) installed, this does one file:

```
ffmpeg -i song.mp3 -c:a libvorbis -q:a 5 song.ogg
```

[Audacity](https://www.audacityteam.org/) can do the same through *File → Export →
Export as OGG*.

### 3. Tell the mod the folder exists

Open `sources.json` in a text editor. It looks something like this:

```json
[
  {
    "Id": "game",
    "Name": "Vintage Story's own music",
    "Enabled": false
  },
  {
    "Id": "vintagesymphony",
    "Name": "Vintage Symphony music",
    "Enabled": true,
    "Url": "https://api.github.com/repos/Dantoes/VintageSymphony-Assets-Release/releases",
    "Compatible": "1.1",
    "Installed": "1.1.0"
  }
]
```

Add an entry for your folder. It needs only an id and a name, and the id must
match the folder exactly:

```json
[
  {
    "Id": "game",
    "Name": "Vintage Story's own music",
    "Enabled": false
  },
  {
    "Id": "vintagesymphony",
    "Name": "Vintage Symphony music",
    "Enabled": true,
    "Url": "https://api.github.com/repos/Dantoes/VintageSymphony-Assets-Release/releases",
    "Compatible": "1.1",
    "Installed": "1.1.0"
  },
  {
    "Id": "my-tunes",
    "Name": "My tunes",
    "Enabled": true
  }
]
```

Watch the commas: every entry but the last ends with one. If the file will not
load, the mod logs an error and falls back to its defaults rather than losing your
music, so a typo is not fatal, but it does mean your tracks will not appear until
it is fixed. A JSON checker such as [jsonlint.com](https://jsonlint.com/) will
point at the line.

You do not need a `Url`. That is only for sources that can be downloaded, which
is covered further down.

### 4. Start a world and let the mod write the track list

Load any world. The mod finds your files and, because there is no track list yet,
writes one for you at `music/tracks.json`, with every track tagged as **calm**.
The game's log (`Logs/client-main.log` in the data folder) says so:

```
Found 2 untracked music file(s) in 'my-tunes'. Listed them as Calm in .../tracks.json - edit it to say when they should play.
```

### 5. Say when each track should play

Open `tracks.json` and change the tags:

```json
{
  "tracks": [
    { "file": "hearth.ogg", "situations": ["calm", "idle", "keep"], "title": "By the Hearth" },
    { "file": "war-drums.ogg", "situations": ["fight"], "title": "War Drums", "priority": 1.5 }
  ]
}
```

Each track can have:

| field | required | what it does |
|---|---|---|
| `file` | yes | The file name as it sits in the `music` folder, extension included. |
| `situations` | yes | One or more of the names in the next section. |
| `title` | no | What `.music info` shows. Defaults to the file name. |
| `priority` | no | Higher wins when several tracks fit the moment. `1` is normal; `2` is "nearly always pick this one"; `0.5` is "only sometimes". |
| `volume` | no | `0` to `1`, on top of the global volume slider. Use it to quieten a track that was mastered loud. |

The mod reads `tracks.json` when a world loads, so after editing, leave to the
main menu and load the world again. Unknown situation names are reported in the
log and ignored, and a track whose file is missing is skipped with a message
saying which.

### 6. Check it is working

In chat, `.music info` shows what is playing and which source it came from.
`.music next` skips to another track. `.music debug` puts up an overlay with the
situation scores, which is the quickest way to see why the mod is choosing what it
is choosing.

## The situations

Tag a track for every situation it would suit. Most gentle tracks fit two or
three of the peaceful ones; the dramatic ones are usually a single tag.

| name | when it plays |
|---|---|
| `calm` | Nothing is wrong. No enemies about, no rift nearby, you have not been hurt lately, and you are not holding a weapon. Everyday music. |
| `idle` | You are staying in one place: building, crafting, farming. |
| `adventure` | You are a long way from home, more than about 400 blocks from a bed you placed. |
| `cave` | You are deep and in the dark below the natural surface. Sunlight or a nearby bed talks it down, and a sealed room switches it off entirely. |
| `keep` | You are in a room you built under the ground: sealed the way a cellar is, below the natural terrain. Made for underground bases. |
| `danger` | Something hostile is within about 25 blocks, or a rift is close, or you were hurt a moment ago. Tension, not combat. |
| `fight` | An enemy you can see is right on you, attacking or being attacked. |
| `dead` | You died. |
| `temporalstorm` | A temporal storm is running. |

A few things worth knowing:

- **Home is a bed.** The mod learns where home is from beds you place while it
  is installed. Beds that were already there before the mod are not known to it,
  so if `adventure` never plays, place a bed.
- **`keep` needs a sealed room.** The mod uses the same room detection the game
  uses for cellars: walls all round, and a door counts as a wall. The game sizes
  rooms at fourteen blocks a side, so a great hall bigger than that reads as
  open. Inside a keep the cave music stops even if you have no `keep` tracks at
  all; then the mod falls back to your `calm` and `idle` tracks.
- **`fight`, `danger`, `dead` and `temporalstorm` are urgent.** They take over
  quickly and keep playing while the situation lasts. The peaceful ones play a
  track, rest a while, and play another.
- There is also a `silence` situation in the list. It is what the mod uses to
  stay quiet while a resonator is playing near you. There is no point tagging a
  track for it.

## How a track gets chosen

When a situation wins, the mod gathers every track tagged for it, sets aside the
ones that have played recently, and draws one. The draw is weighted by
`priority` with some randomness on top, so a `1.5` track plays more often than a
`1` but does not drown it out. If none of the winning situation's tracks may play
right now, the mod moves down to the next best situation rather than going quiet.

## Sharing a pack with other people

A source with a `Url` can be downloaded from inside the game. Anyone can add
one by pressing *Add a source...* under `.music config` and pasting its address,
and update or remove it from there afterwards.

The download has to be a zip. Put a `music` folder inside it with the `.ogg`
files and `tracks.json`, like this:

```
my-tunes.zip
└── music/
    ├── tracks.json
    ├── hearth.ogg
    └── war-drums.ogg
```

A zip with the files loose at the top level works too. Then host it somewhere
with a direct link that ends in the file, and give people that link.

If you keep the pack on GitHub, publish it as a **release** with the zip attached
and give people the releases address instead:

```
https://api.github.com/repos/<you>/<repo>/releases
```

The mod then offers the newest release, shows a version, and can update in
place. Tag releases with a version like `v1.0` so the mod can tell them apart. It
needs at least two numbers: `v1.0` works, `v1` does not.

Whatever you share, keep the credits with the music. Add an `attributions.txt`
next to the tracks naming who made them and under what licence; the mod carries
it along with the pack.

### As a mod on the ModDB

The other way is to ship the pack as an ordinary content mod, the way most
Vintage Story things are shared. The same `tracks.json` works there. Lay the
mod out like this, with a folder under `assets` named after your pack, letters
and digits only like a mod id:

```
my-tunes.zip
├── modinfo.json
└── assets/
    └── mytunes/
        └── music/
            ├── tracks.json
            ├── hearth.ogg
            └── war-drums.ogg
```

with a `modinfo.json` of the usual shape:

```json
{
  "type": "content",
  "modid": "mytunes",
  "name": "My Tunes",
  "version": "1.0.0",
  "dependencies": { "game": "1.22.0", "vintagesymphonyforked": "1.2.5" }
}
```

That is all. Anyone who installs the mod gets the music: Vintage Symphony
finds the `tracks.json` among the mod's assets, lists the mod as a source in
`.music config` with "comes with the mod" against it, and plays it. There is
nothing to download and nothing to edit. If they uninstall the mod, the entry
goes with it. The game's `musicconfig.json` format works the same way inside
a mod, for the finer controls in the next section.

## The game's own format, for finer control

The simple `tracks.json` covers most needs. If you want a track only at night,
only in winter, only in the cold, or only far from spawn, use the game's own
`musicconfig.json` instead. Put it in the `music` folder in place of
`tracks.json`; if both exist, `musicconfig.json` wins.

```json
{
  "tracks": [
    {
      "$type": "VintageSymphony.Engine.MusicTrack, VintageSymphony",
      "file": "midwinter-night",
      "title": "Midwinter Night",
      "situation": "calm|idle",
      "minHour": 20,
      "maxHour": 5,
      "minSeason": 0.97,
      "maxSeason": 1.0,
      "maxTemperature": 0,
      "minSunlight": 0,
      "priority": 1.2
    }
  ]
}
```

Differences from `tracks.json`:

- Every entry needs the `$type` line exactly as shown.
- `file` is the name **without** `.ogg`.
- Situations go in one string, separated by `|`.
- **Set `"minSunlight": 0`** unless you mean a track for daylight only. The
  game's default is 5, which quietly stops a track playing at night or underground.

The knobs, all optional:

| field | range | meaning |
|---|---|---|
| `minHour`, `maxHour` | 0 to 24 | Time of day. A range that crosses midnight, like 20 to 5, works. |
| `minSeason`, `maxSeason` | 0 to 1 | The year as a fraction. The game's own tracks use spring 0.22 to 0.47, summer 0.47 to 0.73, autumn 0.73 to 0.97, and winter from 0.97 round to 0.22. |
| `minTemperature`, `maxTemperature` | °C | The temperature where you stand, right now. |
| `minWorldGenTemperature`, `maxWorldGenTemperature` | °C | The climate the area was generated with, ignoring season and time of day. |
| `minRainFall` | 0 to 1 | How much it is raining right now. |
| `minWorldGenRainfall`, `maxWorldGenRainfall` | 0 to 1 | How wet the region is by nature: desert versus rainforest. |
| `minLatitude`, `maxLatitude` | 0 to 1 | Distance from the equator, 0 at the equator, 1 at the pole. |
| `minSunlight`, `maxSunlight` | 0 to 32 | Sunlight reaching you, after roofs and depth. |
| `minDaylight`, `maxDaylight` | 0 to 2 | Brightness of the sky itself: night is near 0, noon near 1. |
| `distanceToSpawnPoint` | blocks | Only play at least this far from the world spawn. |
| `priority`, `volume` | | As in `tracks.json`. |
| `disableCooldown` | true/false | Let this track repeat without waiting its turn. |

## When something is not working

- **Nothing from your source plays.** Check `sources.json` lists it, its
  `Id` matches the folder name exactly, and `Enabled` is `true`. Then look in
  `Logs/client-main.log` for lines mentioning your source's id; every skipped
  track says why.
- **A track plays only in daylight.** You are using `musicconfig.json` and left
  out `"minSunlight": 0`.
- **The wrong situation keeps winning.** Turn on `.music debug` and watch the
  scores. If `danger` is high indoors, something is lurking outside. If `cave`
  is high in your base, the room is not sealed, or is bigger than the game
  will call a room.
- **You edited a file and nothing changed.** Track lists are read when a world
  loads. Go back to the main menu and load it again.
- **The log says `Could not read sources.json ... Using the default music sources`.**
  The file has a JSON error. It is left in place for you to fix; find the line,
  usually a missing or extra comma, and reload the world.
