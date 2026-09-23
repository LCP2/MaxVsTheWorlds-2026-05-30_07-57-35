// MV-910: minimal native bridge for MaxWorlds.Core.IosDeviceStateProbe. Unity's own APIs
// (Application.targetFrameRate, QualitySettings.vSyncCount, FrameTimingManager) cannot see iOS's own
// thermal-mitigation governor or Low Power Mode, so this exposes NSProcessInfo's readings directly.
// Convention-based "Plugins/iOS" folder — Unity compiles this into iOS builds only.

#import <Foundation/Foundation.h>

extern "C" {

int _MvThermalState(void)
{
    return (int)[[NSProcessInfo processInfo] thermalState];
}

bool _MvIsLowPowerModeEnabled(void)
{
    return [[NSProcessInfo processInfo] isLowPowerModeEnabled] ? true : false;
}

}
