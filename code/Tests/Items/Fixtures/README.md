# Shield binary compatibility fixtures

Generated during TS-220 using the unmodified Core serializers from pre-rename main
commit `196c0da`. Item protocol 21, configuration protocol 41 and resume protocol 3
carry numeric identities/catalog positions rather than item names. These fixtures
contain a damaged world wall (ID 1, 875 HP), damaged rear armor (ID 2, 925 HP),
300 kg configuration, 90-second lifetime and spawn weight 3. They test exact
decode/re-encode preservation after the canonical rename, including nested state.

No credentials, transport subjects or external player data are included.
