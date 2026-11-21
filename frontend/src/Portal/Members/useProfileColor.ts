import { useEffect, useState } from 'react';

// The picture is read at this size: enough for its overall colour, cheap to read.
const SAMPLE_SIZE = 16;

// The average colour of a picture, as #RRGGBB; undefined until it has loaded, or when it can't be read.
function averageColor(image: HTMLImageElement) {
  const canvas = document.createElement('canvas');
  canvas.width = SAMPLE_SIZE;
  canvas.height = SAMPLE_SIZE;
  const context = canvas.getContext('2d');

  if (!context) {
    return undefined;
  }

  context.drawImage(image, 0, 0, SAMPLE_SIZE, SAMPLE_SIZE);
  const pixels = context.getImageData(0, 0, SAMPLE_SIZE, SAMPLE_SIZE).data;
  const sums = [0, 0, 0];

  for (let i = 0; i < pixels.length; i += 4) {
    sums[0] += pixels[i];
    sums[1] += pixels[i + 1];
    sums[2] += pixels[i + 2];
  }

  const count = pixels.length / 4;

  return `#${sums
    .map((sum) =>
      Math.round(sum / count)
        .toString(16)
        .padStart(2, '0')
    )
    .join('')}`;
}

// The colour behind a member's header when they have no Discord banner: the accent colour they chose in Discord, or,
// as Discord does for a profile without one, the colour of their picture. Discord's picture server lets pages read its
// pictures; one that can't be read leaves the header dark.
function useProfileColor(accent: string | null, avatar: string | null) {
  const [pictureColor, setPictureColor] = useState<string>();

  useEffect(() => {
    if (accent || !avatar) {
      return undefined;
    }

    let current = true;
    const image = new Image();
    image.crossOrigin = 'anonymous';

    image.onload = () => {
      try {
        if (current) {
          setPictureColor(averageColor(image));
        }
      } catch {
        // The picture's server didn't allow reading it: the header stays dark.
      }
    };

    image.src = avatar;

    return () => {
      current = false;
    };
  }, [accent, avatar]);

  return accent ?? pictureColor;
}

export default useProfileColor;
