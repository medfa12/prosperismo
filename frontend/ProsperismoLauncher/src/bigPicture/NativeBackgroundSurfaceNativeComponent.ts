/* eslint-disable @react-native/no-deep-imports -- codegenNativeComponent is exposed through RN's internal utility path. */
import type {ViewProps} from 'react-native';
import codegenNativeComponent from 'react-native/Libraries/Utilities/codegenNativeComponent';

interface NativeBackgroundSurfaceProps extends ViewProps {}

/**
 * Transparent until a renderer publishes a valid shared BGRA particle frame.
 * The shell's base layer and selected-title artwork remain mounted beneath it.
 */
export default codegenNativeComponent<NativeBackgroundSurfaceProps>(
  'ProsperismoNativeBackground',
);
