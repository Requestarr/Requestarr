import { throttle } from 'lodash';
import { RefObject, useEffect, useState } from 'react';
import useMeasure from 'Helpers/Hooks/useMeasure';
import dimensions from 'Styles/Variables/dimensions';

const bodyPadding = parseInt(dimensions.pageContentBodyPadding);
const bodyPaddingSmallScreen = parseInt(
  dimensions.pageContentBodyPaddingSmallScreen
);

// How often a scroll of the page moves the list, as Sonarr's index views throttle it.
const SCROLL_THROTTLE_MS = 10;

// Widths closer than this to the last one don't redraw the list, as Sonarr's posters skip them.
const RESIZE_THRESHOLD = 20;

function windowScrollTop() {
  return document.documentElement.scrollTop || document.body.scrollTop || 0;
}

// Sonarr main's index views (Series/Index/Posters/SeriesIndexPosters, Overview/SeriesIndexOverviews,
// Table/SeriesIndexTable) each give their react-window list the page body's width and the window's height, and move
// it as the page body scrolls (the window, on a phone). The same two effects, kept in one place. onScrollTop moves
// the list to the page's scroll position.
function useIndexScroll(
  scrollerRef: RefObject<HTMLElement>,
  isSmallScreen: boolean,
  onScrollTop: (scrollTop: number) => void
) {
  const [measureRef, bounds] = useMeasure();
  const [size, setSize] = useState({ width: 0, height: 0 });

  useEffect(() => {
    const current = scrollerRef.current;
    const padding = (isSmallScreen ? bodyPaddingSmallScreen : bodyPadding) - 5;
    const fullWidth = isSmallScreen ? window.innerWidth : current?.clientWidth;

    if (fullWidth == null) {
      return;
    }

    const width = fullWidth - padding * 2;

    if (Math.abs(size.width - width) >= RESIZE_THRESHOLD || !size.height) {
      setSize({ width, height: window.innerHeight });
    }
  }, [isSmallScreen, size, scrollerRef, bounds]);

  useEffect(() => {
    const scroller = scrollerRef.current as HTMLElement;
    const listener = isSmallScreen ? window : scroller;

    const handleScroll = throttle(() => {
      const { offsetTop = 0 } = scroller;
      const scrollTop =
        (isSmallScreen ? windowScrollTop() : scroller.scrollTop) - offsetTop;

      onScrollTop(scrollTop);
    }, SCROLL_THROTTLE_MS);

    listener.addEventListener('scroll', handleScroll);

    return () => {
      handleScroll.cancel();
      listener.removeEventListener('scroll', handleScroll);
    };
  }, [isSmallScreen, scrollerRef, onScrollTop]);

  return { measureRef, size };
}

export default useIndexScroll;
