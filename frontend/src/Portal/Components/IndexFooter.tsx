import classNames from 'classnames';
import React, { useContext } from 'react';
import ColorImpairedContext from 'App/ColorImpairedContext';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import styles from './IndexFooter.css';

export interface LegendItem {
  kind: 'danger' | 'default' | 'info' | 'primary' | 'success' | 'warning';
  label: string;
}

interface Statistic {
  title: string;
  data: number;
}

interface IndexFooterProps {
  legend: LegendItem[];

  // Each group is one column of totals.
  statistics: Statistic[][];
}

// Sonarr main's SeriesIndexFooter, for any list: a legend of the colours its labels use, then its totals.
function IndexFooter({ legend, statistics }: IndexFooterProps) {
  const isColorImpaired = useContext(ColorImpairedContext);

  return (
    <div className={styles.footer}>
      <div>
        {legend.map(({ kind, label }) => (
          <div key={label} className={styles.legendItem}>
            <div
              className={classNames(
                styles[kind],
                isColorImpaired && 'colorImpaired'
              )}
            />
            <div>{label}</div>
          </div>
        ))}
      </div>

      <div className={styles.statistics}>
        {statistics.map((group) => (
          <DescriptionList key={group[0].title}>
            {group.map(({ title, data }) => (
              <DescriptionListItem key={title} title={title} data={data} />
            ))}
          </DescriptionList>
        ))}
      </div>
    </div>
  );
}

export default IndexFooter;
