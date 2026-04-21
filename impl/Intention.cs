using System;

namespace gttoduf.impl;

public struct Intention {

    /**
        A stripped down version of what I wrote for my own movement - 
        this represents one discrete state for use in a state machine
        but it's been slightly retooled so that I can just instantiate
        them directly in the movement manager and just use them as flags
    **/

    public bool Trying { get; private set; }
    public bool Doing { get; private set; }
    public short StateTicks { get; private set; }
    public Action EntryTrigger;
    public Action ExitTrigger;

    public Intention() { }

    public Intention TickUp(int amt = 1) {
        if(StateTicks < 1024)
            StateTicks += (short)amt;
        return this;
    }

    public Intention TickDown(int amt = 1) {
        if(StateTicks > -1024)
            StateTicks -= (short)amt;
        return this;
    }

    public void SetTrying(bool trying = true) {
        Trying = trying;
    }

    public void SetTryingIfNotDoing(bool trying = true) {
        if(Doing != trying) Trying = trying;
    }

    public void SetDoing(bool doing = true) {
        if(!Doing && doing) EntryTrigger?.Invoke();
        if(Doing && !doing) ExitTrigger?.Invoke();
        Doing = doing;
    }

    public void SetDoingIfNotTrying(bool doing = true) {
        if(Trying != doing) Doing = doing;
    }

    public void SetTryingAndDoing(bool value) {
        Trying = value;
        Doing = value;
    }

    public void Reset() {
        if(!Doing) {
            Trying = false;
            StateTicks = 0;
        }
    }

    public void ResetTicks() {
        StateTicks = 0;
    }

    public readonly bool IsExpected() {
        return Trying || Doing;
    }

    public readonly bool IsTryingButNotDoing() {
        return Trying && !Doing;
    }

    public readonly bool IsDoingButNotTrying() {
        return !Trying && Doing;
    }

    public readonly override string ToString() {
        return ("trying? " + Trying + " doing? " + Doing).ToLowerInvariant() + ", (" + StateTicks + "t)";
    }

    public static implicit operator bool(Intention i) {
        return i.Doing;
    }
}