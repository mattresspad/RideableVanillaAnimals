# Repeat the roster trial

These are developer verification tools, not player content. Use Windows x64, SDK 11.0.100, the official Meitou v0.22.1 game package and Transport playtest.6. Run only one game client at a time. The exact game ProductVersion is checked before launch. No game DLLs are committed or packaged.

Set MEITOU_DIR to the extracted game directory and KENSHI_PATH to your installed Kenshi directory. Put dotnet on PATH. Build and stage a fresh isolated profile:

```powershell
python tools/package.py
dotnet build qa/Ride/Ride.csproj -c Release
./qa/prepare.ps1 -TrialRoot $trial -FrameworkZip $frameworkZip -PackZip ./dist/Rideable-Vanilla-Animals-0.1.0-playtest.1.zip
./qa/run.ps1 -Game $env:MEITOU_DIR -TrialRoot $trial -Cases packs-and-goats,dogs-raptors-and-crabs,spiders-and-gorillos,large-animals,cleanser,leviathan,trader,small,wild
```

The first seven cases cover 43 records, including the stock Wandering Trader's Pack Bull. The small case repeats three younger animals. Wild checks refusal of an unowned Garru. Native repeats a bagged Pack Beast and bareback goat with save/reload; use it in a separate fresh profile for additional worker-count checks. Giant-profiles repeats the two largest animals; set RVA_TRACK_BROADSIDE=1 for a rider-following view.

The observer stages exact animal records in a player-owned squad, uses the framework's existing controls and ordinary movement commands, and measures actual rendered pelvis and saddle transforms independently. It requires correct facing/scale, errors below 0.08 units, native anchors, expected saddle count, and unchanged bags/cargo. Save cases check reload, remount, dismount and subsequent on-foot movement. Setup teleportation only arranges the fixture; it is not player functionality. Headless here still renders game frames and captures PNGs.

Per-case client.log, rides.jsonl, screenshots and saves stay under the supplied trial root; never commit the entire trial. Private renderer hooks are confined to this test observer and may need adapting for later engine releases. The player pack contains no code. Results from one/four-worker live rides should be compared by outcomes and tolerances, not assumed to produce identical frame counts.
