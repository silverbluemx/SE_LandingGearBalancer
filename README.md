# SE_LandingGearBalancer
Landing Gear Balancer script for Space Engineers

Adjustable landing gear pistons to compensate for uneven terrain and land with your ship horizontal.

Version 1.0 - 2024-12-21 - First public release
Version 1.1 - 2025-01-03 - Added a timer before turning off, for commands "retract" and "extend"

Designed for use with:
- Up to 8 landing gear kits (1 kit = piston+landingpad+camera)

## Functions
- use one downward-facing camera on each landing kit to measure distance from ground
- adjust the lenght of all landing gear pistons to compensate
- provide warning if the terrain is too uneven
- in long legs mode, all pistons are extended by default, and they retract if needed due to uneven terrain
- in short legs mode, all pistons are retracted by default, and they extend if needed due to uneven terrain
- the script activates autolock on each leg, and turns itself off when one has locked

## Installation
- Set up to 8 landing kits, with a magnetic plate on a piston, and a downward facing camera
as close as possible (but with a direct, unobstructed view of the ground)
- Create groups for each landing kit, named LGB_kit1, LGB_kit2 etc. with the 3 items in each. The name of the items themselves is irrelevant.
- (optional) Install an LCD screen with the proper name (see below) to see what the script does
- install the script in a programmable block
- recompile the script if needed to let it autoconfigure itself

## Usage
- When close the the ground, activate the script in long legs or short legs mode
- Keep the ship horizontal yourself, or use another script (ex: Flight Assist) to do it
- The script start to check the ground below an altitude of 100m (configurable)
- If the ground is too uneven, thescript does it best and the LCD turns red.

## Command line arguments
- off: turns the script off (ex : when already landed, etc.) without moving the pistons
- on: activate the leg balancer, preferring long legs
- on_longlegs: activate the leg balancer, preferring long legs
- on_shortlegs: activate the leg balancer, short long legs
- retract: retract all legs, and turn the script off
- extend: extend all legs, and turn the script off
