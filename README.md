<h1>Concept:</h1>    
  
Runtime meshcutting combined with FOV zoom (incompatible with Fontaine's FOV fix) to reduce the FPS cost of ADSing with optic scopes.

<h1>Current features:</h1>    
  
Reliable magnification level through scope json (may not be exactly same as PiP).  
Weapon scaling using absolute jank approximations.  
Customizable LODbias + Culling settings while ADSing.  
Whitelist system.  
Mesh caching in raid only.  
Auto-exclusion of nightvision and thermal scopes.  
Customizable ADS speed.  
Per scope meshcutter settings.  
Per scope reticle size.  

<h1>Roadmap:</h1>    
  
-~~Remove cache system or rework it, performance impact needs to be inspected~~ Auto delete cache at end of raid option  
-~~More targeted meshes (weapon,attachments...)~~ Done  
-~~Find way to work across more scopes~~ Done  
-~~More consistent zoom FOVs~~ Done, using the multiplier stored in the item json  
-~~Make default whitelist~~ Auto disable for variable scopes, NV and thermal scopes  
-~~Remove useless options/code~~ Done  
-~~Support variable scopes~~ lmao fuckoff  
-~~Improve weapon scaling offset/multiplier or method~~ Feels right  
-~~Improve reticle scaling~~ Per scope reticle size  
-~~Make meshcutting more precise.~~ Per scope meshcutting   
-~~Add whitelist system~~ Done   
-~~Add per scope config~~ **(maybe even per mag level ?)**    
-~~Hide reticle when swaying across scope housing/weapon~~ Done  
-~~Find way to make vignette stick to scope.~~ Kind of, all the guns scale approximately the same so one vignette/shadow size fits all  
-Hide multiple options under advanced BepInEx settings  
-Find best settings for different scopes. (Help welcome)  
-Release 0.1  

<h1>FPS Comparison:</h1>     
  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20173054.png "Picture in Picture Scope")  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20173102.png "PiP-Disabler")  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20174242.png "Picture in Picture Scope")  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20174303.png "PiP-Disabler")  
