#import <UIKit/UIKit.h>
#import <AVFoundation/AVFoundation.h>

// Haptics for Dogpatch.Feel.Haptics. Generators are created once and kept prepared so the first
// tap of a smash chain has no latency.
static UIImpactFeedbackGenerator *impactGenerators[5];
static UINotificationFeedbackGenerator *notificationGenerator;
static UISelectionFeedbackGenerator *selectionGenerator;

static UIImpactFeedbackGenerator *ImpactGenerator(int style)
{
    if (style < 0 || style > 4) style = 1;
    if (impactGenerators[style] == nil)
    {
        UIImpactFeedbackStyle styles[5] = { UIImpactFeedbackStyleLight, UIImpactFeedbackStyleMedium, UIImpactFeedbackStyleHeavy, UIImpactFeedbackStyleSoft, UIImpactFeedbackStyleRigid };
        impactGenerators[style] = [[UIImpactFeedbackGenerator alloc] initWithStyle:styles[style]];
    }
    return impactGenerators[style];
}

extern "C"
{
    void DogpatchHapticImpact(int style, float intensity)
    {
        UIImpactFeedbackGenerator *g = ImpactGenerator(style);
        [g impactOccurredWithIntensity:intensity];
        [g prepare];
    }

    void DogpatchHapticNotify(int type)
    {
        if (notificationGenerator == nil) notificationGenerator = [[UINotificationFeedbackGenerator alloc] init];
        UINotificationFeedbackType types[3] = { UINotificationFeedbackTypeSuccess, UINotificationFeedbackTypeWarning, UINotificationFeedbackTypeError };
        [notificationGenerator notificationOccurred:types[type < 0 || type > 2 ? 0 : type]];
        [notificationGenerator prepare];
    }

    void DogpatchNativeLog(const char *message)
    {
        NSLog(@"%s", message);
    }

    // Game audio plays through the loud speaker and ignores the ring/silent switch, also while the
    // Operator's microphone is open: PlayAndRecord with DefaultToSpeaker, mixed with other apps.
    void DogpatchAudioSessionSpeaker()
    {
        AVAudioSession *session = [AVAudioSession sharedInstance];
        NSError *error = nil;
        AVAudioSessionCategoryOptions options = AVAudioSessionCategoryOptionDefaultToSpeaker | AVAudioSessionCategoryOptionMixWithOthers | AVAudioSessionCategoryOptionAllowBluetoothA2DP;
        if (![session setCategory:AVAudioSessionCategoryPlayAndRecord mode:AVAudioSessionModeDefault options:options error:&error])
            NSLog(@"[Audio] session category failed: %@", error);
        if (![session setActive:YES error:&error])
            NSLog(@"[Audio] session activate failed: %@", error);
        NSLog(@"[Audio] session %@ options %lu route %@", session.category, (unsigned long)session.categoryOptions, session.currentRoute.outputs.firstObject.portType);
    }

    // After the microphone starts or stops: puts the session back on PlayAndRecord with
    // DefaultToSpeaker, MixWithOthers and A2DP only if Unity changed the category, dropped an option
    // or moved the route to the receiver. Runs on a serial background queue so the main thread never
    // blocks on AVAudioSession, and checks again 0.3 s later in case Unity's change lands after
    // Microphone.IsRecording flips.
    static void ReassertAudioSession(const char *when)
    {
        AVAudioSession *session = [AVAudioSession sharedInstance];
        AVAudioSessionCategoryOptions options = AVAudioSessionCategoryOptionDefaultToSpeaker | AVAudioSessionCategoryOptionMixWithOthers | AVAudioSessionCategoryOptionAllowBluetoothA2DP;
        BOOL categoryOk = [session.category isEqualToString:AVAudioSessionCategoryPlayAndRecord] && (session.categoryOptions & options) == options;
        BOOL routeOk = ![session.currentRoute.outputs.firstObject.portType isEqualToString:AVAudioSessionPortBuiltInReceiver];
        BOOL changed = NO;
        if (!categoryOk || !routeOk)
        {
            NSError *error = nil;
            if ([session setCategory:AVAudioSessionCategoryPlayAndRecord mode:AVAudioSessionModeDefault options:options error:&error]) changed = YES;
            else NSLog(@"[Audio] session category failed: %@", error);
        }
        NSLog(@"[Audio] session %@ options %lu route %@ (%s check: %s)", session.category, (unsigned long)session.categoryOptions, session.currentRoute.outputs.firstObject.portType, when, changed ? "reasserted" : "unchanged");
    }

    void DogpatchAudioSessionReassert()
    {
        static dispatch_queue_t queue;
        static dispatch_once_t once;
        dispatch_once(&once, ^{ queue = dispatch_queue_create("dogpatch.audiosession", DISPATCH_QUEUE_SERIAL); });
        dispatch_async(queue, ^{ ReassertAudioSession("mic change"); });
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.3 * NSEC_PER_SEC)), queue, ^{ ReassertAudioSession("0.3 s later"); });
    }

    void DogpatchHapticSelection()
    {
        if (selectionGenerator == nil) selectionGenerator = [[UISelectionFeedbackGenerator alloc] init];
        [selectionGenerator selectionChanged];
        [selectionGenerator prepare];
    }
}
