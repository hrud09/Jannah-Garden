// Native photo sharing and gallery saving for Jannah Garden.
//
// Called from NativeIntegration.IOSNativePhoto; answers back through UnitySendMessage into
// NativePhotoService.OnNativePhotoResult. The GameObject name, the method name and the two entry point
// names below are a contract with the C# side — change them together or not at all.
//
// Two things this file needs from the Xcode project, both added automatically by
// NativePhotoBuildPostProcessor: Photos.framework, and NSPhotoLibraryAddUsageDescription in Info.plist.
// Saving crashes at the permission prompt without that key, which is why it is injected rather than
// left to be remembered.

#import <UIKit/UIKit.h>
#import <Photos/Photos.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static NSString* const kJannahPhotoReceiver = @"NativePhotoService";
static NSString* const kJannahPhotoCallback = @"OnNativePhotoResult";

// Matches NativePhotoService.CancelledMessage. The game stays quiet when it sees this rather than
// telling the player that dismissing the share sheet went wrong.
static NSString* const kJannahPhotoCancelled = @"cancelled";

#pragma mark - Helpers

static NSString* JannahPhotoString(const char* value)
{
    return value ? [NSString stringWithUTF8String:value] : @"";
}

static NSString* JannahPhotoEscape(NSString* value)
{
    NSString* escaped = [(value ?: @"") stringByReplacingOccurrencesOfString:@"\\" withString:@"\\\\"];
    escaped = [escaped stringByReplacingOccurrencesOfString:@"\"" withString:@"\\\""];
    escaped = [escaped stringByReplacingOccurrencesOfString:@"\n" withString:@" "];
    return [escaped stringByReplacingOccurrencesOfString:@"\r" withString:@" "];
}

/// Sends the outcome to Unity. Safe to call from any queue — PHPhotoLibrary answers on its own.
static void JannahPhotoReport(NSString* action, BOOL success, NSString* message)
{
    NSString* json = [NSString stringWithFormat:@"{\"action\":\"%@\",\"success\":%@,\"message\":\"%@\"}",
                                                action,
                                                success ? @"true" : @"false",
                                                JannahPhotoEscape(message)];

    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage([kJannahPhotoReceiver UTF8String], [kJannahPhotoCallback UTF8String], [json UTF8String]);
    });
}

/// The controller a sheet can be presented from. Found through the key window rather than Unity's own
/// view controller, so this keeps working when the game is embedded in a host app that owns the window.
static UIViewController* JannahPhotoTopViewController(void)
{
    UIWindow* keyWindow = nil;

    for (UIScene* scene in UIApplication.sharedApplication.connectedScenes)
    {
        if (![scene isKindOfClass:[UIWindowScene class]]) continue;

        for (UIWindow* window in ((UIWindowScene*)scene).windows)
        {
            if (!window.isKeyWindow) continue;
            keyWindow = window;
            break;
        }

        if (keyWindow != nil) break;
    }

    // A host app may keep its window off the active scene list; fall back to whatever is on screen.
    if (keyWindow == nil)
    {
        for (UIScene* scene in UIApplication.sharedApplication.connectedScenes)
        {
            if (![scene isKindOfClass:[UIWindowScene class]]) continue;
            keyWindow = ((UIWindowScene*)scene).windows.firstObject;
            if (keyWindow != nil) break;
        }
    }

    UIViewController* controller = keyWindow.rootViewController;
    while (controller.presentedViewController != nil) controller = controller.presentedViewController;

    return controller;
}

static BOOL JannahPhotoFileExists(NSString* path)
{
    return path.length > 0 && [NSFileManager.defaultManager fileExistsAtPath:path];
}

#pragma mark - Share

extern "C" void _jannahPhotoShare(const char* filePath, const char* caption)
{
    NSString* path = JannahPhotoString(filePath);
    NSString* text = JannahPhotoString(caption);

    dispatch_async(dispatch_get_main_queue(), ^{
        if (!JannahPhotoFileExists(path))
        {
            JannahPhotoReport(@"share", NO, @"The photo is no longer on the device");
            return;
        }

        UIViewController* presenter = JannahPhotoTopViewController();
        if (presenter == nil)
        {
            JannahPhotoReport(@"share", NO, @"Sharing is unavailable right now");
            return;
        }

        NSMutableArray* items = [NSMutableArray array];
        if (text.length > 0) [items addObject:text];
        [items addObject:[NSURL fileURLWithPath:path]];

        UIActivityViewController* sheet =
            [[UIActivityViewController alloc] initWithActivityItems:items applicationActivities:nil];

        sheet.completionWithItemsHandler = ^(UIActivityType activityType,
                                             BOOL completed,
                                             NSArray* returnedItems,
                                             NSError* activityError) {
            if (activityError != nil)
            {
                JannahPhotoReport(@"share", NO, activityError.localizedDescription);
                return;
            }

            JannahPhotoReport(@"share", completed, completed ? @"" : kJannahPhotoCancelled);
        };

        // On iPad an activity sheet is a popover and must be anchored, or presenting it throws.
        UIPopoverPresentationController* popover = sheet.popoverPresentationController;
        if (popover != nil)
        {
            popover.sourceView = presenter.view;
            popover.sourceRect = CGRectMake(CGRectGetMidX(presenter.view.bounds),
                                            CGRectGetMidY(presenter.view.bounds),
                                            0.0,
                                            0.0);
            popover.permittedArrowDirections = 0;
        }

        [presenter presentViewController:sheet animated:YES completion:nil];
    });
}

#pragma mark - Save

static void JannahPhotoWriteToLibrary(NSString* path)
{
    [[PHPhotoLibrary sharedPhotoLibrary] performChanges:^{
        [PHAssetChangeRequest creationRequestForAssetFromImageAtFileURL:[NSURL fileURLWithPath:path]];
    }
        completionHandler:^(BOOL success, NSError* error) {
            JannahPhotoReport(@"save",
                              success,
                              success ? @"" : (error.localizedDescription ?: @"Could not save the photo"));
        }];
}

extern "C" void _jannahPhotoSaveToGallery(const char* filePath, const char* albumName)
{
    // The album name is Android's; iOS add-only access cannot see or create albums, so the photo goes
    // to the camera roll. Kept in the signature so both platforms share one C# call.
    (void)albumName;

    NSString* path = JannahPhotoString(filePath);

    dispatch_async(dispatch_get_main_queue(), ^{
        if (!JannahPhotoFileExists(path))
        {
            JannahPhotoReport(@"save", NO, @"The photo is no longer on the device");
            return;
        }

        // Add-only: the game never reads the player's library, so iOS asks for the narrower permission
        // and the prompt is the gentler "add to your photos" one.
        [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly
                                                   handler:^(PHAuthorizationStatus status) {
            if (status != PHAuthorizationStatusAuthorized && status != PHAuthorizationStatusLimited)
            {
                JannahPhotoReport(@"save", NO, @"Photo access is needed to save to your gallery");
                return;
            }

            JannahPhotoWriteToLibrary(path);
        }];
    });
}
