#import <UIKit/UIKit.h>

#if __has_include(<UnityFramework/UnityAppController.h>)
#import <UnityFramework/UnityAppController.h>
#elif __has_include("UnityAppController.h")
#import "UnityAppController.h"
#endif

@interface YargSceneDelegate : UIResponder <UIWindowSceneDelegate>
@property (strong, nonatomic) UIWindow *window;
@end

@implementation YargSceneDelegate

- (void)scene:(UIScene *)scene willConnectToSession:(UISceneSession *)session options:(UISceneConnectionOptions *)connectionOptions {
    if (![scene isKindOfClass:[UIWindowScene class]]) {
        return;
    }
    UIWindowScene *windowScene = (UIWindowScene *)scene;
    UnityAppController *controller = GetAppController();
    if (controller) {
        if (!controller.window) {
            [controller createUI];
        }
        if (controller.window) {
            controller.window.windowScene = windowScene;
            controller.window.frame = windowScene.coordinateSpace.bounds;
            self.window = controller.window;
            [controller.window makeKeyAndVisible];
        }
    }
}

- (void)windowScene:(UIWindowScene *)windowScene didUpdateCoordinateSpace:(id<UICoordinateSpace>)previousCoordinateSpace interfaceOrientation:(UIInterfaceOrientation)previousInterfaceOrientation traitCollection:(UITraitCollection *)previousTraitCollection {
    UnityAppController *controller = GetAppController();
    if (controller && controller.window) {
        controller.window.frame = windowScene.coordinateSpace.bounds;
    }
}

@end

@interface YargAppController : UnityAppController
@end

@implementation YargAppController

- (UISceneConfiguration *)application:(UIApplication *)application configurationForConnectingSceneSession:(UISceneSession *)connectingSceneSession options:(UISceneConnectionOptions *)options {
    UISceneConfiguration *config = [[UISceneConfiguration alloc] initWithName:@"Default Configuration" sessionRole:connectingSceneSession.role];
    config.delegateClass = [YargSceneDelegate class];
    return config;
}

@end

IMPL_APP_CONTROLLER_SUBCLASS(YargAppController)
