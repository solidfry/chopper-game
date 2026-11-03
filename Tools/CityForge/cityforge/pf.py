"""
Bridge to PartForge: CityForge uses the same geometry builder (Unity space, swatch per face) and the same palette
material (one 5 x 5 swatch texture per kit), so buildings and choppers share one pipeline and one look.
"""
import os
import sys

PARTFORGE = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "PartForge"))
if PARTFORGE not in sys.path:
    sys.path.insert(0, PARTFORGE)

from partforge import geo, palette  # noqa: E402,F401
