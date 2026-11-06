# Sailwind - BetterRudder

A BepInEx mod for **Sailwind** that isolates rudder performance to the player's active vessel and dynamically adjusts rudder authority and sideways drag.

---

## Features
* **Active Vessel Isolation:** Utilizes `GameState.currentBoat` to ensure patches apply only to the vessel you are currently sailing.
* **Smart Index Matching:** Uses strict numeric index matching (`(index)`) to prevent substring collisions between similar boat names.
* **Rate-Limited Logging:** Keeps your log files clean by throttling diagnostic output.

---

## Installation
1. Install [BepInEx](https://builds.bepinex.dev/) for Sailwind.
2. Download the latest `BetterRudder.dll` from the **Releases** section.
3. Place `BetterRudder.dll` into your `Sailwind/BepInEx/plugins/` folder.
4. Launch the game!

---

## License
Distributed under the MIT License. See `LICENSE` for more information.