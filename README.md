# GTTOD, with the sauce

This is a BepInEx mod for Get to the Orange Door that replaces all stock character controller code with a custom, 
unified implementation. This mod was written in the interest of improving the game and to test what I've been
experimenting with in an actual in-game setting.

## A smoother GTTOD chracter controller

The stock character controller in GTTOD is implemented using rigidbody physics. Friciton, drag, and mass calculations 
performed by Unity rigidbodies tend to crush any amount of nuanced control you have over how exactly they move, no
matter how deliberate you are with your implementation. GTTOD's controller attempts to skirt around this issue
by selectively enabling/disabling kinematic mode on the player's rigidbody depending on the movement state, as well
as having an entirely separate controller just for handling wallruns.

To fix these issues, I've completely bypassed all stock movement code belonging to doorguy and replaced it with
my own discrete kinematic controller.

## what it do:
- Completely replaces all vanilla character controller code, replacing rigidbody velocity/friction with kinematic stuff where possible
- Ties the modded code into vanilla stuff like landcannons and monkey bars so that they behave predictibly
- Implements wall scanning and trajectory prediction to allow vaulting and wallrunning to respond to the presence of surfaces before they're touched
- Re-implements gamefeel stuff to be easier on the eyes (less camera/weapon sway overall)


## whats left (todo)
- Implement vaulting with a new timing system so that accidental vaults don't consume all your speed
- Centralize important movement variables so that they can be adjusted via commands and/or the plugin config file
- Cache the state of the ac_CharacterController instance so that it can be restored to allow the mod to be toggled at runtime
