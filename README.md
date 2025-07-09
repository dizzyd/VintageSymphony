# Vintage Symphony

This is a fork of the original Vintage Symphony, adding a few additional features and updating the mod to 1.20.x

## New Features & Fixes

Updated to 1.20.x

Music played on death ("Death Situation")

Fixed bugs related to the .music commands

## Adding Music
(Haven't checked if Vintage Symphony Assets download automatically still, but they should.)

Following your first install of Vintage Symphony, load a world once, then continue:

Find "vintage-symphony-assets_1.0.0.zip" in your mod folder.

Unzip it somewhere.

From here, you can add/remove tracks. Navigate to the folder with the music and musicconfig.json (located inside assets/vintage-symphony/music)

### To Remove a track:

Remove the track's entry in musicconfig.json, and delete its .ogg file.

Alternatively, modify its musicconfig.json entry to Edit a track.

### To Add a track:

Create a new entry in musicconfig.json. Copy/Paste one of the other entries and edit it:

- "$type": "VintageSymphony.Engine.MusicTrack, VintageSymphony" <- Leave this the same.
- "source": "you can put anything in here" <- Displayed with .music info, typically the origin of the music: artist, show, etc.
- "file": "file_name" (Excluding the .ogg) It must: be lowercase and use dashes, and the file must be .ogg, and you do not include the file's extension. Special characters don't tend to work well, the alphabet is reliable. Additonally, THE MUSIC FILE MUST BE .OGG, NO VIDEO
- "title": "you can put anything in here" <- The title is displayed with .music info, or on the .music debug display.
- "onPlayList": "survival|creative" <- This can be left alone unless you have a specific use case.
- "situation": "idle|calm|adventure|danger|cave|temporalstorm|dead" <- Note: Songs that play during temporalstorm will not sound right at all, due to the auditorial glitch effects from the temporal storm. Additionally, at the current time cave music tracks do not appear to work.
- "volume": 1.0 <- Whatever volume you desire. 1 tends to work most of the time. Can be 0.5, 1.2, etc.
- Extra Properties. No comprehensive lists, scroll through the default music entries to find examples of the various properties. One notable property is "priority", this can be useful to prioritize/deprioritize a track.

Once you are done adding/modifying/removing tracks, add the assets folder (with your modified music) and the modinfo.json to a zip archive, then replace the original zip file in your mods folder. Recommended to use the exact same name for your new zip.

If the game crashes, and the logs say something about vintage symphony, (It will mention config, along with the exact line that is causing the problem) look for the error causing the issue and fix it in the musicconfig. Some common errors are misspelling the file in the config vs the file system, or missing a comma, bracket or ".

A non .ogg file will crash the game if it gets played.

Weird/non-standardized names (lowercase, dashes, no special characters) will tend to cause problems more often than not.

### Extra Properties of Tracks:

Here is a list of the extra properties a track can have as of the latest version, with example values (Some properties may be missing, but I believe all of them are here):

- "priority": 1.00; The priority of the track when selecting between available. Recommended to increase this while adding additional restricitions using the other properties.
- "minHour": 0; The minimum hour of the day before this track can be played. I believe it is a 24 hour day.
- "maxHour": 12; The latest hour of the day this track can be played. Similar to minHour.
- "minTemperature": -99: The lowest the temperature can be before this track can play.
- "maxTemperature": 99: The highest the temperature can be before this track can play.
- "minWorldGenTemperature": -99: Rather than the current temperature, this uses the temperature of the area set during worldgen.
- "maxWorldGenTemperature": 99: Same as above, but for a maximum.
- "minWorldGenRainfall": 0.0: The amount of rainfall the area recieves. Minimum.
- "maxWorldGenRainfall": 1.0: The maximum of recieved rainfall.
- "minSunlight": 0: The minimum amount of sunlight. This is from 'TrackedPlayerProperties.sunSlight', and I do not know if this is sunlight only or includes player lights.
- "maxSunlight": 32: This may or may not work. Needs testing. Maximum amount of sunlight.
