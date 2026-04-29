# GTTOD, with the sauce

This is a BepInEx mod for Get to the Orange Door that replaces all stock character controller code with a custom, 
unified implementation. This mod was written in the interest of improving the game and to test what I've been
experimenting with in an actual in-game setting.

## A smoother GTTOD chracter controller

Unity rigidbodies do not like to cooporate when you try to feed them finely tuned
velocity vectors, (as per their nature) and there's frighteningly little information on the 
internet about solving this problem. GTTOD attempts to skirt around this issue by doing a couple
things - namely by toggling kinematic mode on the fly, and by using some complex logic in its 
character controller to counteract the effects of rigidbody friction and mass.

I've completely bypassed all stock movement code belonging to doorguy and replaced it with my own discrete 
kinematic controller.

## what it do:
- Acts as a drop-in replacement for all movement code in GTTOD
- Replaces rigidbody-based velocity and friction calculations with custom kinematic code
- Restores wallkicks and introduces new wall dash behaviour
- Ties the modded code into vanilla stuff like landcannons and monkey bars so that they behave predictibly
- Implements wall scanning and trajectory prediction to allow vaulting and wallrunning to respond to the presence of surfaces before they're touched
- Re-implements gamefeel stuff to be easier on the eyes (less camera/weapon sway overall)


## whats left (todo)
- Implement vaulting with a new timing system so that accidental vaults don't consume all your speed
- Centralize important movement variables so that they can be adjusted via commands and/or the plugin config file
- Cache the state of the ac_CharacterController instance so that it can be restored to allow the mod to be toggled at runtime
