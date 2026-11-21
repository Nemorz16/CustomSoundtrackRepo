# Custom Soundtrack 0.5.0

## What changed

- Settings grouped into General, Hotkeys, Boss Music, Combat, Death, Extraction, Hideout, Main Menu, Personal Player, Raid Ambient, UI Replacement, Environment and Injury Speech. Debug groups appear only when General → Advanced Debug is on.
- Existing configuration keys and keybinds are retained; only their presentation changes. Feature switches remain visible, and their subordinate settings hide when switched off. If a menu does not refresh immediately, close and reopen it.
- Master Volume now multiplies soundtrack, boss, hideout and replacement UI levels, plus environmental levels when environmental controls are enabled. For example, Master 50% × Boss 60% gives 30%. Old configurations may have Master at 60%, so radios/bosses will become quieter until you adjust it.
- Optional Master Fade overrides individual fade times. Combat entry remains immediate. Transitions between categories crossfade; Next/Previous fades out the old track, then fades in the new one.
- Combat End defaults to **Previous**: resume the interrupted Ambient or Personal track at its saved position. Other choices are Ambient, PersonalSaved, PersonalRandom and Silence. Already paused categories stay paused.
- Personal music can temporarily override menu and outcome music using Play Personal. Stop or Play Other returns to the relevant playlist; an interrupted death/extraction track retains its position. Return To Menu dismisses the outcome permanently.
- Startup Track lists supported files across Music folders. Startup tracks use Main Menu volume. After the first track, choose the normal menu playlist, the selected file's folder playlist, or silence. Leaving the menu cancels the temporary startup playlist. Refresh Startup Tracks rescans names; the startup choice applies on the next launch.
- Run Through has its own playlist choice. None means silence, not the vanilla jingle.
- Personal playback offers Sequence/Shuffle and an optional on-screen track announcement.
- WAV, OGG and MP3 are accepted in music folders and UI packs. Invalid files are skipped and logged. MP3 decoding and seeking still need testing in Tarkov; compressed file size does not imply equally small decoded memory use.

## Quick controls

Existing bindings are preserved. Defaults for a fresh config:

| Action | Key | What it controls |
| --- | --- | --- |
| Play / pause | Up Arrow | Current music category |
| Stop | Down Arrow | Current category; Personal returns to other music; hideout switches both radios off |
| Previous / next | Left / Right Arrow | Current category's playlist |
| Play Personal | Home | Start/resume Personal, subject to combat override rules |
| Play Other Music | Insert | Leave Personal for the relevant ambient/combat/menu/outcome playlist |
| Return To Menu | End | Dismiss death/extraction music when back in menus |
| Volume up / down | Page Up / Page Down | Current category, including hideout |
| Open / close player | F10 | Raid-only MP3 overlay |

Change keys under **Hotkeys**. Click a binding and press your preferred key. Boss playback is never controlled by these keys. The overlay retains selectable tracks, search, skins, scrolling, transport controls, volume and Personal-track scrubbing.

## Music folders

All paths below are under `BepInEx/plugins/CustomSoundtrack`. Put files directly inside the folder; ordinary playlists do not scan subfolders.

| Folder | Use and related settings |
| --- | --- |
| `Music/MainMenu` | Menu playlist; Main Menu Music enable, volume and fades |
| `Music/Hideout` | Both custom radios; Hideout enable, volume, shuffle and playback mode |
| `Music/RaidAmbient` | Background raid music; Raid Ambient settings |
| `Music/Personal` | Player-controlled playlist; Personal Player settings |
| `Music/Combat` | Combat playlist; override, triggers, return mode, delay and volume |
| `Music/Death` | Random death track; playback limit 0 means full song through results/menu |
| `Music/Extraction` | Random successful-extraction track; limit 0 means full song through results/menu |
| `Music/Bosses/Shared` | Shared boss playlist |
| `Music/Bosses/Killa`, `Tagilla`, etc. | Individual boss playlists; enable each boss separately |

Music folders refresh on restart. Boss files are catalogued per raid. Empty replacement folders retain existing fallback behaviour; do not use empty folders as an enable/disable switch.

## Hideout direction and loudness

Keep the generator fuelled and switched on.

- **Whole Hideout Playback ON:** room-wide playback, linked off switches, one audible source to prevent doubling.
- **OFF:** original positional radios and independent switches.
- **Directional Volume:** boosts source gain in positional mode. Unity caps source volume, so this alone may not increase the distance you can hear it.
- **Directional Reach:** increases native minimum/maximum distances while retaining direction. Start at 2–4; 1 preserves vanilla distances. Directional controls hide in whole-hideout mode.
- The regular Hideout Volume and Master Volume apply to both modes. The game's own audio settings still apply.

## Boss music and helmet filtering

Enable Boss Music, then enable the desired bosses. Playback remains local to each listener; this update does not synchronise boss playlists between Fika clients.

| Playlist mode | Selection |
| --- | --- |
| PreferIndividual | Individual folder; Shared only if the individual folder is empty |
| IndividualOnly | Only that boss's folder; empty means no track |
| SharedOnly | Only Shared |
| MixAllBossFolders | Random tracks from every individual boss folder plus Shared |

**On Boss Death:** ContinueMusic, StopMusic, Static or Random. Random uses Static Chance, then Silence Chance. Continue Music Chance shows the remainder. If static + silence exceeds 100%, silence is capped to the remaining space. A single random draw is held for each corpse.

**Helmet Muffling** is off by default for controlled testing. It uses Unity's low-pass filter; no edited audio files are required. Lower cutoff Hz means a more muffled sound:

- Killa: one cutoff in all directions, initially 1,800 Hz.
- Tagilla: front cutoff initially 2,200 Hz; rear cutoff 12,000 Hz. The filter blends as the listener moves around his head.
- Smoothing controls how quickly the filter follows changes. These are artistic starting points, not physical helmet measurements.
- **Stop When Helmet Looted** stops music or static once the dead boss's tracked original headwear leaves its slot. It overrides the chosen death behaviour and stays stopped for that corpse. Bosses without tracked headwear are unaffected. Test both local and teammate looting.

## Environmental controls ***

Off by default. Enable **Environment** after removing the separate EnvironmentVolumeControl plugin.

Sliders adjust native **rain/precipitation, wind, BTR movement, airdrop aircraft and airdrop container** sounds. 1 is the native level before Master Volume; 0 mutes that controlled sound. Disabling Environment restores native gain. BTR gunshots are not affected.

These are volume controls, not replacement sound packs. Separate bird/animal/map ambience, breathing, searching and medkit replacements are not implemented in this test. Snow/blizzard coverage through the precipitation/wind system needs verification; there is no independently verified blizzard hook yet.

## Automatic injury speech ***

Off by default. Enable **Injury Speech**, then choose Light Bleed, Heavy Bleed, Fracture and Blacked Limb.

Calls use the selected character's native voice pack and normal in-world speech path. The inspected Fika 2.4.6 implementation sends native spoken phrases to teammates; actual host/client audibility still needs a co-op test. These are not private UI notifications. Normal game speech rules and possible AI reactions remain applicable.

- Light/heavy bleed use **Bleeding**. The native phrase set does not distinguish their severity.
- Arm/leg fractures use **HandBroken / LegBroken**.
- A destroyed limb or stomach uses **HurtHeavy**, not a literal 'blacked limb' line.
- Only newly detected injuries are announced. Existing injuries at spawn are not read aloud.
- Cooldown prevents rapid repeated calls; resolved queued injuries are discarded. Simultaneous injuries sharing a phrase are combined. Existing speech is allowed to finish first.
- Voice-pack availability still applies: a pack missing a requested phrase cannot speak it.
- Native announcements retain the game's character-voice mixing rather than the music mixer. Master 0 prevents new automatic announcements; lower nonzero Master values do not currently scale native speech or teammates' voices.

Enable Injury Debug under Advanced Debug to log detected injuries and phrase requests. Test while a second player listens nearby; hearing it yourself alone does not prove Fika replication.

## UI sound packs

Put a pack in `Sounds/Packs/YourPackName`. Each sound is a flat file, for example `BackpackOpen.wav`; MP3/OGG extensions are also supported. Do not put each sound in its own event subfolder. Select the pack under **UI Replacement → Sound Pack**. Missing events retain vanilla sounds.

After adding/editing files, turn **Refresh Packs** on; it resets itself after scanning. Reopen the configuration menu if its dropdown needs refreshing. Legacy `Sounds/UI`, `Sounds/Chat` and `Sounds/Notifications` remain compatibility paths, but named packs do not require them.

UI Debug shows the actual replacement filepath on screen and in the log. Individual event switches are under the advanced UI debug groups. General Notification stays in the normal UI section.

## Debug and test results

General → Advanced Debug reveals the diagnostics; it does not turn logging on. Disable individual logging options after testing. Report the feature, settings, host/client role and `BepInEx/LogOutput.log` when something fails.

See `TEST_CHECKLIST.md` for the recommended order. `VALIDATION.txt` records compilation and automated results. Automated tests cover playback policy, probability boundaries, filter math and existing radio timing logic, not live game hooks.

## Optional source build

Source is included under `Source/CustomSoundtrack_0.5.0_AudioControls` in the test ZIP. From that folder run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -SptRoot "E:\SPT" -Test -Install
```

This compiles against your installed BepInEx/Unity DLLs. Tests must pass before installation. The installer preserves music and backs up the previous DLL and config. It does not remove EnvironmentVolumeControl automatically.

## Credits

Concept, direction, artwork and in-game testing: **Nemorz / W Russell**.

Implementation, troubleshooting, documentation and automated-test assistance: **OpenAI ChatGPT / Codex**, working iteratively with Nemorz. This project has been developed across many test iterations; a complete verified iteration count is not available, so no exact number is claimed.

Fika.Core and EnvironmentVolumeControl were supplied as compatibility/behaviour references. Their DLLs and source are not bundled in this release. Integration code in this update was written for Custom Soundtrack; reference mod implementations were not copied wholesale.
