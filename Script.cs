using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GUI;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.Remoting.Lifetime;
using System.Text;
using System.Threading;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {


/*
-------------------------------------
LANDING GEAR BALANCER by silverbluemx
-------------------------------------

A script to automatically manage your landing gear pistons to compensate
for uneven terrain and land with your ship horizontal.

Version 1.0 - 2024-12-21 -  First public release
Version 1.1 - 2025-01-03 -  Added a timer before turning off, for commands "retract" and "extend"
Version 1.2 - 2025-07-06 -  Supports up to 100 landing gear kits !
                            Detects and warns about obstructed cameras
                            Shows on screen if in long legs or short legs mode
Version 1.3 - 2025-12-07 -  Added "unlock_retract", "unlock_extend", "unlock" commands
                            Works when landing on grids (ex : large grid landing pad that is not level for some reason)

Designed for use with:
- Up to 100 landing gear kits (1 kit = piston+landingpad+camera)

Functions:
- use one downward-facing camera on each landing kit to measure distance from ground
- adjust the lenght of all landing gear pistons to compensate
- provide warning if the terrain is too uneven
- in long legs mode, all pistons are extended by default, and they retract if needed due to uneven terrain
- in short legs mode, all pistons are retracted by default, and they extend if needed due to uneven terrain

Installation:
- Set up to 100 landing kits, with a magnetic plate on a piston, and a downward facing camera
    as close as possible (but with a direct, unobstructed view of the ground)
- Create groups for each landing kit, named LGB_kit1, LGB_kit2 etc. with the 3 items in each
- The names must be continuous, starting at 1 and going up to 100 (LGB_kit1, LGB_kit2, ..., LGB_kit100).
If there is one missing (ex: LGB_kit1, LGB_kit2, LGB_kit4), the script will stop at LGB_kit2
- (optional) Install an LCD screen with the proper name (see below) to see what the script does
- install the script in a programmable block
- recompile the script if needed to let it autoconfigure itself

Usage:
- When close the the ground, activate the script in long legs or short legs mode
- The script start to check the ground below an altitude of 100m (configurable)
- If the ground is too uneven, the LCD turns red.

Command line arguments:
- off :            turns the script off (ex : when already landed, etc.) without moving the pistons
- on :             activate the leg balancer
- on_longlegs :    activate the leg balancer, preferring long legs
- on_shortlegs:    activate the leg balancer, short long legs
- retract     :    retract all legs, and turn the script off
- extend      :    extend all legs, and turn the script off
- unlock_retract : unlock all gears, then retract
- unlock_extend :  unlock all gears, then extend
- unlock :         unlock all gears, keep the pistons where they are

*/



        public LandingGearBalancer manager;

        public class LGBConfiguration {

            public string lcd_name =  "LGB_LCD";
            // Duration of the timer before turning off, expressed in hundreds of ticks (1 tick = 1/60s).
            // So a value of 3 means rougly 5seconds
            public int turnoff_duration_100s_ticks = 3; 

            
        }

        public class LandingBlocks {
            // All blocks needed for the script
            public List<LandingKit> kits = new List<LandingKit>();
            public List<IMyTerminalBlock> displays;

        }

        public class LandingKit {
            // A landing kit is an adjustable landing leg with :
            // - a camera to measure distance from the ground
            // - a piston to adjust the leg lenght
            // - a gear block (landing gear, magnetic plate, etc.)
            public IMyCameraBlock camera;
            public IMyPistonBase piston;
            public IMyLandingGear gear;
            public bool valid = false;
            public double piston_target=0;
            public double offset =0;
            public bool interference = false;
            public string name;

            private const double ACTIVATION_DISTANCE = 100;
            private const double TOL = 0.02;
            private const float PISTON_SPEED = 0.5f;
            public double distance = ACTIVATION_DISTANCE;

            // Methods
            public void Disable() {
                camera.EnableRaycast = false;
                camera.Enabled = false;
                piston.Enabled = false;
            }

            public void Enable() {
                camera.EnableRaycast = true;
                camera.Enabled = true;
                piston.Enabled = true;
            }

            // Cast a ray with the camera to measure distance from ground
            public void UpdateDistance() {

                if (camera.CanScan(ACTIVATION_DISTANCE)) {

                    MyDetectedEntityInfo radar_return = camera.Raycast(ACTIVATION_DISTANCE);

                    if (radar_return.Type == MyDetectedEntityType.None)
                    {   
                        // No hit, so the distance is the maximum
                        distance=ACTIVATION_DISTANCE;
                        interference = false;
                        
                    } else {

                        // We have detected something, now find what it is
                        bool hit_ground = radar_return.Type ==  MyDetectedEntityType.Planet || radar_return.Type ==  MyDetectedEntityType.Asteroid;
                        bool hit_a_ship = radar_return.Type == MyDetectedEntityType.LargeGrid || radar_return.Type == MyDetectedEntityType.SmallGrid;
                        // If the return is the same entity as one of the kit components, it means that the ray hit a part of the ship itself !
                        bool hit_itself = radar_return.EntityId == camera.CubeGrid.EntityId || radar_return.EntityId == piston.CubeGrid.EntityId || radar_return.EntityId == gear.CubeGrid.EntityId;

                        if (hit_itself) {

                            interference = true;
                            distance=ACTIVATION_DISTANCE;

                        } else if ((hit_a_ship || hit_ground) && radar_return.HitPosition.HasValue) {

                            interference = false;
                            Vector3D hitpos = radar_return.HitPosition.Value;
                            distance = Math.Min(ACTIVATION_DISTANCE,VRageMath.Vector3D.Distance(hitpos,camera.GetPosition()));

                        } else {

                            interference = false;
                            distance=ACTIVATION_DISTANCE;
                        }
                    }
				
			    }
            }

            public void AdjustPiston(double max_distance, double min_distance, bool prefer_long_legs) {
                // - if preferring long legs, it is the maximum distance
                // - if preferring short legs, it is the minimum               

                if (prefer_long_legs) {
                    offset = distance-max_distance; 
                    // negative value, ex : -5 when this landing kit is 5m closer to the ground than the kit that is the furthest
                    piston_target = Math.Max(Math.Min(piston.HighestPosition+offset,piston.HighestPosition),piston.LowestPosition);
                } else {
                    offset = distance-min_distance; 
                    // negative value, ex : -5 when this landing kit is 5m closer to the ground than the kit that is the furthest
                    piston_target = Math.Min(Math.Max(piston.LowestPosition+offset,piston.LowestPosition), piston.HighestPosition);
                }
                

                if (piston.CurrentPosition > piston_target + TOL) {

                    piston.MinLimit = (float)(piston_target);
                    piston.MaxLimit = (float)(piston_target + TOL);
                    piston.Velocity = -PISTON_SPEED;


                } else if (piston.CurrentPosition < piston_target - TOL) {

                    piston.MinLimit = (float)(piston_target - TOL);
                    piston.MaxLimit = (float)(piston_target);
                    piston.Velocity = PISTON_SPEED;

                } else {

                    // The piston stays where it is !
                    piston.Velocity = 0;

                }
            }

            public void Extend() {
                piston.MinLimit = 0;
                piston.MaxLimit = piston.HighestPosition;
                piston.Velocity = PISTON_SPEED;
            }

            public void Retract() {
                piston.MinLimit = 0;
                piston.MaxLimit = piston.HighestPosition;
                piston.Velocity = -PISTON_SPEED;
            }

            public bool CheckLock() {
                if (gear.IsLocked) {
                    return true;
                } else {
                    return false;
                }

            }

            public void ActivateAutoLock() {
                gear.AutoLock = true;
            }

            public void UnLock()
            {
                gear.Enabled = true;
                gear.Unlock();
            }




        }



        public Program() {


            Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100; 
            
            LGBConfiguration config = new LGBConfiguration();
            
            LandingBlocks LGB_system = GetLandingBlocks(config);
            
            manager = new LandingGearBalancer(config, LGB_system);
            manager.init();

        }

        public void Main(string argument, UpdateType updateSource)

        {
            // Process the command given to the programmable block
            // We use exact matches with accepted commands
            if ((updateSource & (UpdateType.Trigger | UpdateType.Terminal)) != 0)
  	            {
                    switch (argument) {
                        case "off":
                            manager.TurnOff();
                            break;

                        case "on":
                            manager.TurnOn(true);
                            break;

                        case "on_longlegs":
                            manager.TurnOn(true);
                            break;

                        case "on_shortlegs":
                            manager.TurnOn(false);
                            break;

                        case "extend":
                            manager.ExtendAll();
                            break;

                        case "retract":
                            manager.RetractAll();
                            break;

                        case "unlock":
                            manager.UnLockAll();
                            break;

                        case "unlock_retract":
                            manager.UnLockAll();
                            manager.RetractAll();
                            break;

                        case "unlock_extend":
                            manager.UnLockAll();
                            manager.ExtendAll();
                            break;
                    }

                }

            if ((updateSource & UpdateType.Update10) != 0) {
                manager.Tick10();
            }

            if ((updateSource & UpdateType.Update100) != 0) {
                manager.Tick100();
            }
            
        }

        
        public LandingBlocks GetLandingBlocks(LGBConfiguration config) {

            // Scan the grid for the appropriately set up landing kits
            // and return a LandingBlocks object

            LandingBlocks blocks = new LandingBlocks();
            List<IMyTerminalBlock> displays= new List<IMyTerminalBlock>();

            // Find all landing kits

            for (int kitNumber=1; kitNumber <= 100; kitNumber++) {

                string kitname = "LGB_kit" + kitNumber.ToString();

                LandingKit tempkit = TryGetLandingKit(kitname);

                if (tempkit.valid) {

                    Echo("Try to append group  "+kitname);

                    blocks.kits.Add(tempkit);
                    Echo("Group  "+kitname + "done !");
                    
                } else {
                    break; // stop at the first invalid kit (dont try to fetch tens of kits that are not there)
                }
            }

            // Find all displays

            GridTerminalSystem.SearchBlocksOfName(config.lcd_name, displays,block => block.IsSameConstructAs(Me));
            blocks.displays = displays;

            Echo("Found  "+blocks.kits.Count() + "kits !");
            Echo("Found  "+displays.Count() + "displays !");

            return blocks;
        }

        public LandingKit TryGetLandingKit(string name) {

            LandingKit kit = new LandingKit();

            IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName(name);
            if (group == null)  {
                Echo("Group "+name+"not found");
                return kit;
            } else {
                Echo("Group "+name+" found !");
            }

            List<IMyCameraBlock> cameras = new List<IMyCameraBlock>();

            group.GetBlocksOfType(cameras);

            List<IMyPistonBase> pistons = new List<IMyPistonBase>();

            group.GetBlocksOfType(pistons);

            List<IMyLandingGear> gears = new List<IMyLandingGear>();
            
            group.GetBlocksOfType(gears);

            if (cameras.Count == 1 & pistons.Count == 1 && gears.Count == 1) {

                Echo("Group "+name+" has the correct setup (1 camera, 1 piston, 1 gear)");
                
                kit.camera = cameras[0];
                kit.piston = pistons[0];
                kit.gear = gears[0];
                kit.valid = true;
                kit.name = name;
                return kit;
                
            } else {
                
                Echo("Group "+name+" has a wrong setup (need exactly : 1 camera, 1 piston, 1 gear)");

                return kit;
            }
        }

        public class LandingGearBalancer {

            LandingBlocks landingblocks;
            LGBConfiguration config;

            bool active = false;
            double maxdist = 0;
            double mindist=0;
            bool prefer_long_legs = true;
            double max_unevenness=0;
            int offtimer = -1;


            public LandingGearBalancer(LGBConfiguration conf, LandingBlocks LGB_system_defined) {
                config = conf;
                landingblocks = LGB_system_defined;
                max_unevenness = ComputeMaxUnevenness();
	        }

            public void Tick10() {
                
                if (active) {

                    // Disable the landing gear manager if at least one of the landing gears is locked
                    bool any_locked = false;
                    foreach (LandingKit kit in landingblocks.kits) {
                        if (kit.CheckLock()) {
                            any_locked = true;
                        };
                    }

                    if (any_locked) {

                        TurnOff();

                    } else {


                        maxdist = 0;
                        mindist = 100;

                        // Measure distance from ground on all kits

                        foreach (LandingKit kit in landingblocks.kits) {
                            kit.UpdateDistance();
                            maxdist=Math.Max(kit.distance,maxdist);
                            mindist=Math.Min(kit.distance,mindist);
                        }


                        // Update piston length on all kits
                        foreach (LandingKit kit in landingblocks.kits) {
                            kit.AdjustPiston(maxdist,mindist,prefer_long_legs);
                        }
                    }

                    

                }

                UpdateDisplays();

            }

            public void Tick100() {
                
                if (offtimer > -1) {

                    offtimer = offtimer - 1;

                    if(offtimer == 0) {
                        TurnOff();
                    }
                }
            }

            public void StartOffTimer() {
                offtimer = config.turnoff_duration_100s_ticks;
            }

            public void TurnOn(bool prefer_long) {
                
                active = true;
                prefer_long_legs = prefer_long;
                foreach (LandingKit kit in landingblocks.kits) {
                    kit.Enable();
                    kit.ActivateAutoLock();
                }
                
            }

             public void TurnOff() {
                
                active = false;
                foreach (LandingKit kit in landingblocks.kits) {
                    kit.Disable();
                }

            }

            public void ExtendAll() {

                active = false;

                foreach (LandingKit kit in landingblocks.kits) {
                    kit.Enable();
                    kit.Extend();
                }

                StartOffTimer();
            }

            public void RetractAll() {

                active = false;

                foreach (LandingKit kit in landingblocks.kits) {
                    kit.Enable();
                    kit.Retract();
                }

                StartOffTimer();
            }

            public void UnLockAll()
            {
                foreach (LandingKit kit in landingblocks.kits) {
                    kit.UnLock();
                }
            }

            public string debug() {
                

                // Debug
                return "";

            }

            public void UpdateDisplays() {
                foreach (IMyTextPanel display in landingblocks.displays) {
                    display.WriteText("LANDING GEAR BALANCER");
                    display.WriteText("\n----------------------", true);

                    if (active) {
                        display.WriteText("\nActive", true);
                    } else if (offtimer>-1) {
                        display.WriteText("\nGlobal extend/retract", true);
                    } else {
                        display.WriteText("\nDisabled", true);
                    }
                    if (prefer_long_legs) {
                        display.WriteText("\nLong legs mode", true);
                    } else {
                        display.WriteText("\nShort legs mode", true);
                    }
                    display.WriteText("\nNb active kits: " + landingblocks.kits.Count, true);
                    display.WriteText("\nGnd unevenness:"+Math.Round(maxdist-mindist,2).ToString("0.00"), true);

                    if (CheckUnevenness()) {
                        display.FontColor = VRageMath.Color.Orange;
                    } else {
                        display.FontColor = VRageMath.Color.White;
                    }

                    foreach (LandingKit kit in landingblocks.kits) {
                        if (kit.interference) {
                            display.WriteText("\nObstructed camera on\n"+kit.name, true);
                        }
                    }


                    
                }

            }

            public void init() {
                foreach (IMyTextPanel textPanel in landingblocks.displays) {
			
                    textPanel.Enabled = true;
                    textPanel.ContentType = ContentType.TEXT_AND_IMAGE;
                    textPanel.Font = "Monospace";
                    textPanel.FontColor = VRageMath.Color.White;
                    
                    
                }
            }

            public bool CheckUnevenness() {
                if ((maxdist-mindist) > max_unevenness) {
                    return true;
                } else {
                    return false;
                }
            }

            private double ComputeMaxUnevenness() {
                if (landingblocks.kits.Count > 0) {
                    return landingblocks.kits[0].piston.HighestPosition-landingblocks.kits[0].piston.LowestPosition;
                } else {
                    return 0;
                }
                
            }

        }






    } // partial class Program : MyGridProgram

}