package net.booxmax.print;
public final class RefreshPolicyTests {
    static void check(boolean value,String label){if(!value)throw new AssertionError(label);}
    public static void main(String[] args) {
        RefreshPolicy p=new RefreshPolicy();for(int i=0;i<20;i++)check(!p.frame(),"off");
        p.configure(3);check(!p.frame()&&!p.frame()&&p.frame(),"every three committed frames");p.requested();
        check(p.requests==1&&p.since==0,"successful request counter");
        check(!p.frame(),"one frame");p.configure(3);check(!p.frame()&&p.frame(),"same setting does not reset cycle");
        p.configure(2);check(!p.frame()&&p.frame(),"changed interval restarts cycle");
        p.configure(0);check(!p.frame(),"disable");p.configure(1);check(p.frame(),"every frame");
        p.reset();check(p.frames==0&&p.requests==0&&p.since==0&&p.every==1,"new session reset retains configuration");
        for(int bad:new int[]{-1,1001}){boolean rejected=false;try{p.configure(bad);}catch(IllegalArgumentException e){rejected=true;}check(rejected,"range validation");}
        System.out.println("PASS: committed-frame full refresh cadence, disable, settings changes, session reset, bounds");
    }
}
