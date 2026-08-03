/* eslint-disable @react-native/no-deep-imports -- codegenNativeComponent is exposed through RN's internal utility path. */
import type {ViewProps} from 'react-native';
import codegenNativeComponent from 'react-native/Libraries/Utilities/codegenNativeComponent';

interface NativeBackgroundSurfaceProps extends ViewProps {
  /** Home owns the recovered particle/ripple pass; every surface keeps FirstWave. */
  particleOverlayEnabled: boolean;
}

/**
 * Always renders the translated 12.40 FirstWave plate. When enabled, the
 * producer's recovered particle/ripple frame is additively composited above it.
 */
export default codegenNativeComponent<NativeBackgroundSurfaceProps>(
  'ProsperismoNativeBackground',
);
