"""Patch workflow for Aether, a Terraria utility client.

Aether's own code lives in client/. Changes to game files live in patches/, one unified
diff per file. The user's Terraria.exe is decompiled into .base/, and the patches are
applied to build the editable src/ tree.
"""
