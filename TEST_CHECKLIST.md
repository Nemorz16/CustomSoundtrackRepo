# 0.5.0 test checklist

Test one section at a time. Keep the previous DLL/config backup until these pass. New Environment, Injury Speech and Helmet Muffling features default off.

## 1. Baseline and menus

- Confirm the log loads **Custom Soundtrack 0.5.0** with no duplicate radio/UI plugin.
- Check existing music, skins, keybinds and UI pack selection survived.
- Check groups and shorter setting names. Turn Advanced Debug on/off: the toggle stays visible; diagnostics hide/reappear.
- Switch a feature off/on: its enable switch stays visible and its subordinate settings follow it.
- Test main-menu playback and UI replacements first. No early vanilla music should reappear.
- Master 0 silences menu, custom UI, radios and bosses; raising it restores them. Native injury speech has the documented mixing exception.
- Choose a startup file, restart, and verify it plays once before the chosen after-startup action. Try a file from a boss/hideout folder. Leave the menu during loading and check recovery is clean.

## 2. Hideout

- Generator fuel and power on. Enter once from menu and again after a raid.
- Test radio sync, context Next/Previous and arrow transport controls.
- Whole Hideout ON: one off switch stops both, with no duplicate volume.
- Whole Hideout OFF: compare Directional Reach 1 and 4 from a fixed location. Direction should remain audible; Volume adjusts gain.
- Toggle the mode back and check native distances are restored.

## 3. Raid playback and outcomes

- Ambient → combat → Previous returns to the same Ambient track/time.
- Personal → combat → Previous returns to the same Personal track/time without pressing Play.
- Test each Combat Override and Combat End choice, including Silence and Random Personal.
- Throw a grenade well outside the explosion radius: combat should start on release. Test normal and quick throw. Test melee separately.
- Pause, stop, previous and next target the currently playing category; bosses ignore controls.
- Die/extract: full outcome track persists through results to menus. Press Play Personal, then Stop/Play Other: the original outcome resumes rather than restarting or choosing another track.
- Return To Menu permanently dismisses an outcome. Test Run Through choices when convenient.
- Test WAV, OGG and MP3 independently, including MP3 scrubbing and track-end handling.

## 4. Bosses

- Killa: enable Helmet Muffling, compare 22,000 Hz to 1,800 Hz at a fixed distance. Check filtering remains similar around him.
- Tagilla: compare his front and rear. Check smoothing while moving around him.
- Kill either boss. Test Continue, Stop, Static, then Random.
- In Random, test 100% Static, 100% Silence and both 0%; check displayed remainder never goes negative.
- Loot the helmet/mask: music/static stops. Test teammate looting in Fika too.
- Test IndividualOnly, SharedOnly, PreferIndividual fallback and MixAllBossFolders using recognisably different songs.
- Confirm hideout music still works after the raid.

## 5. Environment

- Remove the separate EnvironmentVolumeControl plugin for this test, then enable integrated Environment.
- Test Rain and Wind from 1 → 0 → 1 while the relevant sound is present. It must recover without restarting.
- Repeat for BTR movement, airplane and airdrop container when available.
- Change Master Volume with Environment on. Disable Environment: native levels should return.
- Note weather/season when testing snow or blizzard; coverage is not yet verified separately.

## 6. Injury announcements — host and client

- Both players run the test mod if both want automatic calls. Enable Injury Speech on the speaking player.
- Have a teammate stand nearby. Acquire a new light bleed, heavy bleed, fracture, then blacked limb on separate occasions.
- Confirm the selected character voice is used and the teammate hears spatial speech once, without duplicate echo.
- Test both Fika host and client as the injured player.
- Multiple injuries must respect cooldown and avoid repeating the same phrase rapidly. Healing a queued injury should cancel its pending call.
- Death should cancel pending calls. Existing injuries when spawning should not announce immediately.
- If silent, enable Injury Debug and provide LogOutput.log, selected voice, injury type and host/client role.
