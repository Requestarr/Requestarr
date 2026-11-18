import React, { ReactNode } from 'react';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './SetupPage.css';

interface SetupStepProps {
  number: number;
  title: string;
  isDone: boolean;
  children: ReactNode;
}

// One step of the first-run setup: its number and title, a tick once it's done, and what to do (or what was done).
function SetupStep({ number, title, isDone, children }: SetupStepProps) {
  return (
    <FieldSet
      legend={translate('SetupStepTitle', { number, title: translate(title) })}
    >
      {isDone && (
        <div className={styles.done}>
          <Icon name={icons.CHECK} kind={kinds.SUCCESS} />
          {translate('SetupStepDone')}
        </div>
      )}

      {children}
    </FieldSet>
  );
}

export default SetupStep;
