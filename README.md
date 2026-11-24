# Rideable Vanilla Animals

A data-only content pack for [Meitou Transport](https://github.com/Meitou-Modding-Ecosystem/MeitouTransport). It supplies seats for 43 stock animal records in 22 fits. Transport supplies riding; this pack supplies animal choices, fit settings and original CC0 saddle art.

**Compatibility candidate:** MeitouClient v0.22.1 Windows x64 with Transport v0.3.0-playtest.6. The 43-record compatibility pass passed; see [RESULTS.md](RESULTS.md) for measurements and visual limitations. Workshop publication is a later step.

## Play

Install Meitou and [Transport playtest.6](https://github.com/Meitou-Modding-Ecosystem/MeitouTransport/releases/tag/v0.3.0-playtest.6), then extract the animal pack into Kenshi/mods. Enable **Meitou Transport -> Rideable Vanilla Animals**. No Maestro, Husbandry or dialogue framework is required.

Select a conscious humanoid beside a living animal in your squad. Use **F10 -> Mount nearby animal**, then ordinary movement orders on the animal. **F10 -> Dismount** requires suitable ground and returns selection to the rider. The pack does not tame animals, grant ownership or add spawns. Named and normally wild types need existing squad ownership. The Pack Beast Dead record was tested using a living instance, not a corpse.

For a stock start, try **The Wandering Trader** and its Pack Bull. Do not enable Garru Saddle alongside this pack unless you deliberately want the later pack's fit to override overlapping records. Large/small animals use the same human-sized rider: feet, fur, shells and bags may overlap. Numerical attachment accuracy is not a guarantee of ideal art contact.

## Author or contribute

The editable mod is in `mods/Rideable Vanilla Animals/`. Its existing pack ID, fit IDs, animal records and filenames are preserved. See [AUTHORING.md](AUTHORING.md), [qa/README.md](qa/README.md) and [LICENSING.md](LICENSING.md).

Run `python tools/package.py` to audit and package the data files. No C# compilation is needed to build the player pack. C# under qa is solely an observation/test driver and never ships in the player ZIP.

Report the game and Transport versions, animal name/record, load order and reproduction steps in [Issues](https://github.com/mattresspad/RideableVanillaAnimals/issues).
