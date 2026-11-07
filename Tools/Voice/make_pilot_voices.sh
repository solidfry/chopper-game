#!/bin/bash
# Placeholder pilot voice lines (plan section 5b, step 5). Needs espeak-ng and ffmpeg (apt-get install espeak-ng ffmpeg).
# Writes Assets/Sound/Voice/Pilots/<CALLSIGN>_Select.ogg (announcer says the call sign) and <CALLSIGN>_Ability.ogg
# (the pilot on the radio when the ability starts). Setup step 1 links them to the pilot assets (empty slots only).
# Replace with recorded or licensed voices later: keep the file names, or set the clips on the pilot assets.
set -e
cd "$(dirname "$0")"
OUT=../../Assets/Sound/Voice/Pilots
WORK=$(mktemp -d)
mkdir -p "$OUT"

make() { # name, text, voice, speed, pitch, style
  espeak-ng -v "$3" -s "$4" -p "$5" -w "$WORK/raw.wav" "$2"
  if [ "$6" = announcer ]; then
    F="highpass=f=90,lowpass=f=7000,acompressor=threshold=-18dB:ratio=4:attack=5:release=80,aecho=0.8:0.6:60|120:0.35|0.2"
  else
    F="highpass=f=350,lowpass=f=3200,acrusher=bits=10:mix=0.4,acompressor=threshold=-20dB:ratio=6:attack=3:release=60"
  fi
  ffmpeg -nostdin -loglevel error -y -i "$WORK/raw.wav" -af "$F,apad=pad_dur=0.25,loudnorm=I=-16:TP=-1.5:LRA=7" \
    -ar 44100 -ac 1 -c:a libvorbis -q:a 5 "$OUT/$1.ogg"
}

# CALLSIGN|spoken call sign|radio line|voice variant|pitch
while IFS='|' read -r sign spoken line voice pitch; do
  make "${sign}_Select" "$spoken." en-us+m3 115 18 announcer
  make "${sign}_Ability" "$line" "en-us+$voice" 170 "$pitch" radio
done <<'LINES'
APOTHECARY|Apothecary|Nanite cloud, deployed.|f2|60
VIPER|Viper|Venom belt, loaded.|m4|40
PYRE|Pyre|Napalm away!|m6|45
WRAITH|Wraith|Going ghost.|f4|55
BULWARK|Bulwark|Aegis up!|m2|20
HOTWIRE|Hot wire|Overclock!|f3|65
OVERWATCH|Overwatch|Ping. Contacts marked.|m1|40
HEX|Hex|Blackout!|f5|50
SCRAPPER|Scrapper|Stripping down!|m5|35
RAMJET|Ram jet|Ramming speed!|m7|30
LINES
rm -rf "$WORK"
echo "Wrote $(ls "$OUT" | grep -c ogg) clips to $OUT"
