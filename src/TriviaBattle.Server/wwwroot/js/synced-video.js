// Plays a video in sync on every screen in the room.
//
// The server sends a cue: { id, url, startAtMs }, meaning "this video's first frame is at server
// time startAtMs". Each screen knows the server time (ScreenConnection.serverNow), so each one
// can work out exactly where the video should be right now and keep itself there:
//   - before startAtMs: loaded and paused on the first frame
//   - at startAtMs:     play
//   - joined late:      jump straight to the right spot
//   - drifting:         nudge the playback speed slightly (or jump, if far off)
//
// Usage:
//   const intro = new SyncedVideo(videoElement, () => connection.serverNow());
//   intro.play(cue, { muted: true, onEnded, onError });
//   intro.stop();

class SyncedVideo {
    static JUMP_IF_OFF_BY_S = 0.3;     // further out than this: seek
    static NUDGE_IF_OFF_BY_S = 0.04;   // between this and the above: speed up/slow down a little

    constructor(video, getServerNow) {
        this.video = video;
        this.getServerNow = getServerNow;
        this.cue = null;
        this.timer = null;
    }

    /**
     * The usual way to use this class: call on every render with state.cue.
     * Shows and plays the video while there's a cue, hides it when the cue goes away or the video ends.
     */
    follow(cue, { muted = true } = {}) {
        if (!cue) {
            if (this.cue) this.stop();
            this.video.hidden = true;
            return;
        }
        if (this.cue?.id === cue.id) return;

        this.video.hidden = false;
        this.play(cue, {
            muted,
            onEnded: () => { this.video.hidden = true; },
            onError: () => { this.video.hidden = true; console.warn('Could not load', cue.url); },
        });
    }

    play(cue, { muted = true, onEnded = () => {}, onError = () => {} } = {}) {
        if (this.cue && this.cue.id === cue.id) return; // already handling this cue

        this.stop();
        this.cue = cue;
        this.video.muted = muted;
        this.video.src = cue.url;
        this.video.preload = 'auto';
        this.video.onended = () => { this.stop(); onEnded(); };
        this.video.onerror = () => { this.stop(); onError(); };
        this.video.load();
        this.timer = setInterval(() => this.keepInSync(), 50);
    }

    stop() {
        clearInterval(this.timer);
        this.timer = null;
        this.cue = null;
        this.video.pause();
        this.video.onended = this.video.onerror = null;
    }

    keepInSync() {
        const shouldBeAt = (this.getServerNow() - this.cue.startAtMs) / 1000; // seconds into the video

        if (shouldBeAt < 0) return; // not time yet: stay paused on the first frame

        if (this.video.paused) {
            if (shouldBeAt > SyncedVideo.NUDGE_IF_OFF_BY_S) this.video.currentTime = shouldBeAt;
            this.video.play().catch(err => {
                // Browsers block sound until someone clicks, unless Chromium was started with
                // --autoplay-policy=no-user-gesture-required (see tools/launch-screens.ps1).
                // Better to play silently than not at all.
                if (err.name === 'NotAllowedError' && !this.video.muted) {
                    console.warn('Autoplay with sound blocked; playing muted.');
                    this.video.muted = true;
                }
            });
            return;
        }

        const drift = this.video.currentTime - shouldBeAt; // positive = ahead
        if (Math.abs(drift) > SyncedVideo.JUMP_IF_OFF_BY_S) {
            this.video.currentTime = shouldBeAt;
            this.video.playbackRate = 1;
        } else if (Math.abs(drift) > SyncedVideo.NUDGE_IF_OFF_BY_S) {
            this.video.playbackRate = drift > 0 ? 0.95 : 1.05;
        } else {
            this.video.playbackRate = 1;
        }
    }
}
