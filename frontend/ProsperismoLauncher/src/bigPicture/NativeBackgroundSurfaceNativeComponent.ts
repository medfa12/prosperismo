/* eslint-disable @react-native/no-deep-imports -- RNW codegen specs require these canonical internal type imports. */
import type {ViewProps} from 'react-native/Libraries/Components/View/ViewPropTypes';
import codegenNativeComponent from 'react-native/Libraries/Utilities/codegenNativeComponent';

interface NativeBackgroundSurfaceProps extends ViewProps {}

/**
 * Transparent until a renderer publishes a valid shared BGRA frame. The PNG
 * sequence remains mounted beneath this native surface as the safe fallback.
 */
export default codegenNativeComponent<NativeBackgroundSurfaceProps>(
  'ProsperismoNativeBackground',
);
