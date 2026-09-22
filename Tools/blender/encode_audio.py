"""Vorbis-encode a recording, through Blender's audio library.

    Blender --background --factory-startup --python Tools/blender/encode_audio.py \
        -- in.wav out.ogg [kbps]

macOS ships no Ogg encoder (afconvert writes AAC and ALAC, not Vorbis), and the
system Python has only numpy, so the music packer borrows Blender for this the
same way the texture and model packers borrow it for images and meshes. Unity
plays .ogg everywhere, and a 100 MB WAV has no business in the repository.
"""
import os
import sys

import aud

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]
kbps = int(argv[2]) if len(argv) > 2 else 112

sound = aud.Sound(src)
rate, channels = sound.specs
sound.write(dst, int(rate), min(2, channels), aud.FORMAT_FLOAT32,
            aud.CONTAINER_OGG, aud.CODEC_VORBIS, kbps * 1000)
print(f"encoded {os.path.basename(dst)}  {sound.length / rate:.1f} s  "
      f"{os.path.getsize(dst) / 1e6:.1f} MB  ({kbps} kbps vorbis)")
