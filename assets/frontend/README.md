# Frontend media

The TS-85 MenuShell presentation uses `Menu no music.ogv` as its runtime video and
`../audio/project/music/Welcome to the Carnage Circus main menu.mp3` as its
authoritative music. `source/Menu no music.mov` preserves the exact approved source
bytes and is excluded from Godot import by `source/.gdignore`.

Godot's built-in video player supports Ogg Theora rather than the supplied
QuickTime/H.264 stream, so the runtime derivative keeps the original 1920×1080,
24 fps, 2:59.75 presentation while changing only the container/video codec. The
MOV's AAC track was deliberately omitted. The resulting OGV contains one Theora
video stream and no audio stream. At runtime its player volume is also zero as a
defense in depth; the unchanged MP3 plays independently on the Music bus.

Both large video files use Git LFS. `sources.json` records exact hashes, byte sizes,
probe results and the reproducible conversion command. FFmpeg is an external
authoring tool and is not a runtime dependency.
