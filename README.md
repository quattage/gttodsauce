# GTTOD, with the sauce

A BepInEx mod for [Get to the Orange Door](https://store.steampowered.com/app/541200/GTTOD/)  
that replaces all stock character controller code with a custom, unified implementation. 

## A smoother GTTOD chracter controller

Creating a discrete kinematic character controller in a game engine that provides no built-in tools 
to do so can be rather difficult. Unity's default character controller sucks ass big mode, so you're 
either forced to use a dynamic rigidbody or to roll your own collision detection from scratch, and 
there's frighteningly little information on the internet about solving either of these problems. 
GTTOD attempts to skirt around the multitide of issues caused by using a dynamic rigidbody by doing 
a couple things - namely by toggling kinematic mode on the fly depending on the movement state, 
and by using some complex (and, at times, bizarre) logic in its character controller to counteract 
the effects of rigidbody friction and mass.

I've completely bypassed all stock movement code belonging to doorguy and replaced it with my own
dynamic rigidbody-based kinematic controller.

The mod can be toggled in-game by pressing F4.

## what it do:
- Acts as a drop-in replacement for all movement code in GTTOD
- Replaces rigidbody-based velocity and friction calculations with custom kinematic code
- Restores wallkicks and introduces new wall dash behaviour
- Ties the modded code into vanilla stuff like landcannons and monkey bars so that they behave predictibly
- Implements wall scanning and trajectory prediction to allow wallrunning to respond to the presence of surfaces before they're touched
- Re-implements gamefeel stuff to be easier on the eyes (less camera/weapon sway overall)


## whats left (todo)
- Implement vaulting with a new timing system so that accidental vaults don't consume all your speed
- Centralize important movement variables so that they can be adjusted via commands and/or the plugin config file