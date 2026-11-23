import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import accessLevelChoices from '../accessLevelChoices';
import { contentTypeText } from '../portalText';
import { ContentType, PortalAccess } from '../PortalTypes';
import { PortalAction } from '../usePortalAction';
import styles from '../Portal.css';

// Without a limit of their own the member card shows a week, as requests count it.
const DEFAULT_DAYS = 7;

interface AccessRowProps {
  memberId: number;
  type: ContentType;
  access?: PortalAccess;
  action: PortalAction;
}

// One type on the member card: their access level and their limit, each saved on its own. Which Sonarr or Radarr
// takes their requests follows what is asked for, never the member.
function AccessRow({ memberId, type, access, action }: AccessRowProps) {
  const [values, setValues] = useState({
    state: access?.state ?? 'off',
    limit: access?.quotaLimit ?? 0,
    days: access?.quotaDays ?? DEFAULT_DAYS,
  });
  const levels = accessLevelChoices();

  const onChange = useCallback(
    ({ name, value }: { name: string; value: string | number }) => {
      setValues((current) => ({ ...current, [name]: value }));
    },
    []
  );
  const onAccessPress = useCallback(() => {
    action.act(
      `members/${memberId}/access`,
      { contentType: type, state: values.state },
      'PUT'
    );
  }, [action, memberId, type, values]);
  const onLimitPress = useCallback(() => {
    action.act(
      `members/${memberId}/limit`,
      { contentType: type, limit: values.limit, days: values.days },
      'PUT'
    );
  }, [action, memberId, type, values]);

  return (
    <FieldSet legend={contentTypeText(type)}>
      {(['state', 'limit', 'days'] as const).map((name) => (
        <FormGroup key={name}>
          <FormLabel>
            {translate(`PortalEdit${name[0].toUpperCase()}${name.slice(1)}`)}
          </FormLabel>
          <FormInputGroup
            type={name === 'state' ? inputTypes.SELECT : inputTypes.NUMBER}
            name={name}
            value={values[name]}
            values={levels}
            onChange={onChange}
          />
        </FormGroup>
      ))}
      <div className={styles.actions}>
        <Button onPress={onAccessPress}>{translate('PortalSaveAccess')}</Button>
        <Button onPress={onLimitPress}>{translate('PortalSaveLimit')}</Button>
      </div>
    </FieldSet>
  );
}

export default AccessRow;
