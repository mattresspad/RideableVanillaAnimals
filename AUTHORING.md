# Make a pack like this

Transport reads schema-3 transport.json beside the enabled .mod. The .mod declares Meitou Transport.mod as its only dependency. This pack overrides no FCS records. Each animal record appears in exactly one fit.

Edit the JSON directly, or open `mods/Rideable Vanilla Animals` as a Maestro project with Transport enabled. Use a Maestro build supporting the game readers you select: playtest.1 uses v0.20.1 readers; the playtest.2 candidate uses v0.22.1. Maestro is optional and is not a player dependency.

In Maestro, use Transport's Add riding fit action, select the animal records, choose their bone, and adjust saddle/seat transforms. Review, save, reopen and export. Empty art fields mean bareback. The mesh and texture here are CC0; animal meshes remain in the player's Kenshi installation.

For another pack, choose your own folder/.mod name, packId and fit IDs. For updates to this pack, preserve the existing IDs. A custom animal mod must be installed and declared as a dependency. Two packs targeting one animal are resolved by Transport load order; do not assume both fits apply.

Verify adult and smaller/larger examples, bags, turns and slopes. Check dismount, save/reload and ordinary movement after leaving the seat. Keep visible contact/clipping observations separate from transform error.

For this compatibility candidate, tools/package.py intentionally rejects changes to the four historical runtime files. A later fit revision needs a reviewed baseline update and fresh tests; another author can export their own independent pack directly from Maestro.
