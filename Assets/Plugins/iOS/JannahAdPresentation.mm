#import <UIKit/UIKit.h>

// LevelPlay's Unity bridge presents ads from [UIApplication sharedApplication].keyWindow.rootViewController.
//
// Embedded in the Flutter app, the key window can be Unity's own: UnityAppController calls makeKeyAndVisible
// on it, and flutter_embed_unity then moves it to window level -1, underneath Flutter's window. An ad
// presented from there would sit behind Flutter, out of sight.
//
// So make the visible normal-level window key first. In a standalone Unity build that is already Unity's
// window, and this does nothing.
extern "C" void JannahAds_MakeVisibleWindowKey(void)
{
    for (UIScene *scene in UIApplication.sharedApplication.connectedScenes)
    {
        if (scene.activationState != UISceneActivationStateForegroundActive) continue;
        if (![scene isKindOfClass:UIWindowScene.class]) continue;

        for (UIWindow *window in ((UIWindowScene *)scene).windows)
        {
            if (window.isHidden || window.rootViewController == nil) continue;
            if (window.windowLevel != UIWindowLevelNormal) continue;

            if (!window.isKeyWindow) [window makeKeyWindow];
            return;
        }
    }
}
