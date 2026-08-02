import React, {useEffect, useMemo, useRef, useState} from 'react';
import {
  Animated,
  Easing,
  type ImageSourcePropType,
  Platform,
  StyleSheet,
  UIManager,
  View,
} from 'react-native';
import {SHELL_METRICS} from './shellMetrics';
import type {ShellSurface} from './shellState';
import {shellBackgroundPresentation} from './shellBackgroundPresentation';

type NativeBackgroundComponent = React.ComponentType<{
  particleOverlayEnabled: boolean;
  pointerEvents?: 'none';
  style?: object;
}>;

let resolvedNativeComponent: NativeBackgroundComponent | null | undefined;

function nativeBackgroundComponent(): NativeBackgroundComponent | null {
  if (resolvedNativeComponent !== undefined) {
    return resolvedNativeComponent;
  }
  resolvedNativeComponent = null;
  if (Platform.OS !== 'windows') {
    return resolvedNativeComponent;
  }

  // The component is Fabric-only.  Do not evaluate codegenNativeComponent on
  // hosts where the native registration is absent: the ordinary React tree is
  // still a complete, visible fallback on those hosts and in Jest.
  const manager = UIManager as typeof UIManager & {
    hasViewManagerConfig?: (name: string) => boolean;
  };
  try {
    if (!manager.hasViewManagerConfig?.('ProsperismoNativeBackground')) {
      return resolvedNativeComponent;
    }
    resolvedNativeComponent = require('./NativeBackgroundSurfaceNativeComponent').default;
  } catch {
    resolvedNativeComponent = null;
  }
  return resolvedNativeComponent ?? null;
}

function fileSource(path: string | undefined): ImageSourcePropType | undefined {
  return path ? {uri: `file:///${path.replace(/\\/g, '/')}`} : undefined;
}

export interface ShellBackgroundSurfaceProps {
  surface: ShellSurface;
  modalOpen: boolean;
  artworkPath?: string;
}

/**
 * Stable background owner shared by every Big Picture route.  The native view
 * renders Sony's translated FirstWave plate continuously and consumes the
 * out-of-process particle frames only while the recovered HOME state requests
 * them.  Artwork remains an independent low-opacity title layer.
 */
export function ShellBackgroundSurface({
  surface,
  modalOpen,
  artworkPath,
}: ShellBackgroundSurfaceProps) {
  const presentation = shellBackgroundPresentation(surface, modalOpen);
  const NativeBackground = useMemo(nativeBackgroundComponent, []);
  const nextKey = artworkPath ?? 'none';
  const nextSource = fileSource(artworkPath);
  const [current, setCurrent] = useState({key: nextKey, source: nextSource});
  const [previous, setPrevious] = useState<{key: string; source: ImageSourcePropType | undefined}>();
  const crossFade = useRef(new Animated.Value(1)).current;
  useEffect(() => {
    if (current.key === nextKey) {
      return;
    }
    setPrevious(current);
    setCurrent({key: nextKey, source: nextSource});
    crossFade.setValue(0);
    const animation = Animated.timing(crossFade, {
      toValue: 1,
      duration: SHELL_METRICS.titleBackgroundTransitionMs,
      easing: Easing.linear,
      useNativeDriver: true,
    });
    animation.start(({finished}) => {
      if (finished) {
        setPrevious(undefined);
      }
    });
    return () => animation.stop();
  }, [crossFade, current, nextKey, nextSource]);

  return <View pointerEvents="none" style={styles.owner}>
    <View style={styles.basematFallback} />
    {NativeBackground && <NativeBackground
      particleOverlayEnabled={presentation.particleOverlayEnabled}
      pointerEvents="none"
      style={styles.nativeSurface}
    />}
    {previous?.source && <Animated.Image source={previous.source} style={[
      styles.artwork,
      {opacity: crossFade.interpolate({inputRange: [0, 1], outputRange: [0.16, 0]})},
    ]} />}
    {current.source && <Animated.Image source={current.source} style={[
      styles.artwork,
      {opacity: crossFade.interpolate({inputRange: [0, 1], outputRange: [0, 0.16]})},
    ]} />}
  </View>;
}

const styles = StyleSheet.create({
  owner: {
    ...StyleSheet.absoluteFillObject,
    overflow: 'hidden',
  },
  // Visible only until the Fabric drawing surface publishes its first frame.
  // This is a neutral safety plate, not a replacement rendition of Sony's UI.
  basematFallback: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: '#020408',
  },
  nativeSurface: {
    ...StyleSheet.absoluteFillObject,
  },
  artwork: {
    ...StyleSheet.absoluteFillObject,
    resizeMode: 'cover',
  },
});
