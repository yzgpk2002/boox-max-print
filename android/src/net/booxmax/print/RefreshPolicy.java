package net.booxmax.print;

/** Counts committed new frames, never timer checks, duplicate retries or plain redraws. */
final class RefreshPolicy {
    int every=0,since=0;
    long frames=0,requests=0;
    void configure(int value) {
        if(value<0||value>1000)throw new IllegalArgumentException("Invalid full refresh interval");
        if(every!=value){every=value;since=0;}
    }
    void reset(){since=0;frames=0;requests=0;}
    boolean frame(){frames++;if(every==0)return false;if(++since>=every){since=0;return true;}return false;}
    void requested(){requests++;since=0;}
}
