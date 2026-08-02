import React, {useEffect, useRef, useState} from 'react';
import {StyleSheet, View} from 'react-native';
import {
  focusAreaOpacityScale,
  focusLineBody,
  ShellFocusTimeline,
  type ShellFocusSnapshot,
} from './shellHomeMotion';
import {focusColorAt} from './shellFocusShader';

export interface ShellFocusOverlayProps {
  active: boolean;
  width: number;
  height: number;
  radius: number;
  /** FocusStyle.ListItem crops only the source body, before UI3 inflates it. */
  crop?: {left?: number; top?: number; right?: number; bottom?: number};
}

const HIDDEN: ShellFocusSnapshot = {
  state: 'hidden', rect: {x: 0, y: 0, width: 0, height: 0}, radius: 0,
  lineOpacity: 0, areaOpacity: 0, bandWidth: 0, inOutScale: 1,
  warpStretch: 0, travelAngle: 0, moving: 0, shimmer: [0, 0], pressing: 0,
};

/**
 * One UI3 distance-field focus treatment for every non-Home shell surface.
 * This deliberately replaces route-local stroked boxes: the Home renderer and
 * modal/list renderers now share the recovered 200ms show delay, 250ms warp,
 * 300ms moving driver, exterior 3px offset, and radius inheritance.
 */
export function ShellFocusOverlay({active, width, height, radius, crop}: ShellFocusOverlayProps) {
  const timeline = useRef(new ShellFocusTimeline());
  const previous = useRef<number | undefined>(undefined);
  const frame = useRef<number | undefined>(undefined);
  const [snapshot, setSnapshot] = useState<ShellFocusSnapshot>(HIDDEN);
  const left = crop?.left ?? 0;
  const top = crop?.top ?? 0;
  const bodyWidth = Math.max(1, width - left - (crop?.right ?? 0));
  const bodyHeight = Math.max(1, height - top - (crop?.bottom ?? 0));

  useEffect(() => {
    const target = {x: left, y: top, width: bodyWidth, height: bodyHeight};
    if (active) {
      if (timeline.current.snapshot().state === 'hidden') {
        timeline.current.showAt(target, radius);
      } else {
        timeline.current.retarget(target, radius);
      }
    } else {
      timeline.current.hide();
    }
    previous.current = undefined;
    const tick = (now: number) => {
      const prior = previous.current ?? now;
      previous.current = now;
      timeline.current.advance(Math.min(0.05, Math.max(0, (now - prior) / 1000)));
      const next = timeline.current.snapshot();
      setSnapshot(next);
      if (next.state !== 'hidden') {
        frame.current = requestAnimationFrame(tick);
      }
    };
    frame.current = requestAnimationFrame(tick);
    return () => { if (frame.current !== undefined) { cancelAnimationFrame(frame.current); } };
  }, [active, bodyHeight, bodyWidth, left, radius, top]);

  if (snapshot.state === 'hidden') {
    return null;
  }
  const line = focusLineBody(snapshot.rect, snapshot.radius, snapshot.inOutScale);
  const colour = focusColorAt(0.25 + ((snapshot.shimmer[0] + 1) * 0.25));
  const colourRgba = `rgba(${Math.round(colour.r * 255)}, ${Math.round(colour.g * 255)}, ${Math.round(colour.b * 255)}, 0.96)`;
  const washOpacity = snapshot.areaOpacity * focusAreaOpacityScale(snapshot.rect, {width: 1920, height: 1080});
  const shimmerOpacity = Math.max(0, Math.min(0.08 + ((snapshot.shimmer[0] + 1) * 0.045), 0.17));
  return <>
    {washOpacity > 0 && <View pointerEvents="none" style={[styles.wash, {
      left: snapshot.rect.x, top: snapshot.rect.y, width: snapshot.rect.width, height: snapshot.rect.height,
      borderRadius: snapshot.radius, opacity: washOpacity,
    }]}><View style={[styles.shimmer, {opacity: shimmerOpacity}]} /></View>}
    <View pointerEvents="none" style={[styles.line, {
      left: line.rect.x, top: line.rect.y, width: line.rect.width, height: line.rect.height,
      borderRadius: line.radius, borderWidth: Math.max(1, snapshot.bandWidth), borderColor: colourRgba,
      opacity: snapshot.lineOpacity,
    }]} />
  </>;
}

const styles = StyleSheet.create({
  wash: {position: 'absolute', overflow: 'hidden', backgroundColor: 'rgba(255,255,255,0.14)'},
  shimmer: {position: 'absolute', left: '-18%', top: '-18%', width: '136%', height: '136%', backgroundColor: '#fff', transform: [{rotate: '-18deg'}]},
  line: {position: 'absolute', borderStyle: 'solid', zIndex: 2},
});
