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

    void DogpatchHapticSelection()
    {
        if (selectionGenerator == nil) selectionGenerator = [[UISelectionFeedbackGenerator alloc] init];
        [selectionGenerator selectionChanged];
        [selectionGenerator prepare];
    }
}
