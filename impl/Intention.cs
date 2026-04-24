using System;

namespace gttoduf.impl;

/**
    A stripped down version of what I wrote for my own movement - 
    this represents one discrete state for use in a state machine
    but it's been slightly retooled so that they can be instantiated
    directly in the movement manager and used as flags.
**/
public struct Intention {

    public bool Trying { get; private set; }
    public bool Doing { get; private set; }
    public short Ticks { get; private set; }

    public Action EntryTrigger;
    public Action ExitTrigger;

    public readonly bool Expected => Trying || Doing;
    public readonly bool TryingButNotDoing => Trying && !Doing;
    public readonly bool DoingButNotTrying => !Trying && Doing;

    public Intention() { }

    public Intention Tick(int amount = 1) {
        if(amount > 0 && Ticks < 1024)
            Ticks += (short)amount;
        else if(amount < 0 && Ticks > -1024)
            Ticks += (short)amount;
        // TOOD ticking by 0 should be tracked for actions later
        return this;
    }

    public void ResetTicks() {
        Ticks = 0;
    }

    public void SetTrying(bool value = true) {
        Trying = value;
    }

    public void SetTryingIfNotDoing(bool value = true) {
        if(Doing != value) SetTrying(value);
    }

    public void SetDoing(bool value = true) {
        if(!Doing && value) EntryTrigger?.Invoke();
        if(Doing && !value) ExitTrigger?.Invoke();
        Doing = value;
    }

    public void SetDoingIfNotTrying(bool value = true) {
        if(Trying != value) SetDoing(value);
    }

    public void SetTryingAndDoing(bool value) {
        SetTrying(value);
        SetDoing(value);
    }

    public void Reset() {
        SetTryingAndDoing(false);
        ResetTicks();
    }

    public readonly override string ToString() {
        return ("trying? " + Trying + " doing? " + Doing).ToLowerInvariant() + ", (" + Ticks + "t)";
    }

    public static implicit operator bool(Intention i) {
        return i.Doing;
    }
}