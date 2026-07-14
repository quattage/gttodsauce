# GTTOD, with the sauce

A BepInEx mod for [Get to the Orange Door](https://store.steampowered.com/app/541200/GTTOD/)  
that replaces all stock character controller code with a custom, unified implementation. 

## A smoother GTTOD character controller

Creating a discrete kinematic character controller in a game engine that provides no built-in tools 
to do so can be rather difficult. Unity's default character controller sucks ass big mode, so you're 
either forced to use a dynamic rigidbody or to roll your own collision detection from scratch, and 
there's frighteningly little information on the internet about solving either of these problems. 
GTTOD attempts to skirt around the multitide of issues caused by using a dynamic rigidbody by doing 
a couple things - namely by toggling kinematic mode on the fly depending on the movement state, 
and by using some complex (and, at times, bizarre) logic in its character controller to counteract 
the effects of rigidbody friction and mass.

I've completely bypassed all stock movement code belonging to doorguy and replaced it with my own
dynamic rigidbody-based kinematic controller. If you want to look around at the code, the main
loop is in impl/MovementManger.cs (check out FixedUpdate()).

The mod can be toggled in-game by pressing F4. (<-- doesn't actually work yet, but you can use this to verify that the mod is loaded)

## what it do:
- Replaces rigidbody-based velocity and friction calculations with custom kinematic code
- Emulates sourcelike airstrafing and bhopping
- Provides better wall detection that's much more predictable
- All code runs in physics tick, so no more frame dependent behaviours
- Restores wallkicks and introduces new wall dash behaviour
- Ties the modded code into vanilla stuff like landcannons and monkey bars so that they behave predictibly
- Re-implements gamefeel stuff to be easier on the eyes (less camera/weapon sway overall)
- Since all code is new, old bugs are gone, replaced with new ones :)


## whats left (todo)
- Implement vaulting with a new timing system so that accidental vaults don't consume all your speed
- Centralize important movement variables so that they can be adjusted via commands and/or the plugin config file

## known issues
- Wallrun camera rolling can get shaky on uneven terrain
- Edge cases exist where you can get completely stuck in place on walls (????? coco how ??????)
- Spamming the mod toggle button while crouching sinks the player's camera into the ground forever until the game is restarted
- There are some particle effects (especially enemy gibbs) that the player can STILL stand on/collide with, i have no idea why
- The collider's center is fucked up, which causes:
   - doorguy to grow taller when the mod is applied
   - the sliding particle effects are played right in your face
   - doorguy can't walk when the mod is disabled
- when you stop sliding a small puff of smoke spawns at world origin (0, 0, 0) for some reason idk man
- opening the chat while midair allows the player to fucking fly
- standing up when there's a ceiling above your head while crouching lets you just phase through whatever's above you