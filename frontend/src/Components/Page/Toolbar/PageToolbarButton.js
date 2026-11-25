import classNames from 'classnames';
import PropTypes from 'prop-types';
import React from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import styles from './PageToolbarButton.css';

function PageToolbarButton(props) {
  const {
    className,
    label,
    iconName,
    spinningName,
    isDisabled,
    isSpinning,
    ...otherProps
  } = props;

  return (
    <Link
      className={classNames(
        styles.toolbarButton,
        className,
        otherProps.to && styles.isLink,
        isDisabled && styles.isDisabled
      )}
      isDisabled={isDisabled || isSpinning}
      title={label}
      {...otherProps}
    >
      <Icon
        name={isSpinning ? (spinningName || iconName) : iconName}
        isSpinning={isSpinning}
        size={21}
      />

      <div className={styles.labelContainer}>
        <div className={styles.label}>
          {label}
        </div>
      </div>
    </Link>
  );
}

PageToolbarButton.propTypes = {
  // Requestarr: extra style for one button, such as a label wider than the usual button.
  className: PropTypes.string,
  label: PropTypes.string.isRequired,
  iconName: PropTypes.object.isRequired,
  spinningName: PropTypes.object,
  isSpinning: PropTypes.bool,
  isDisabled: PropTypes.bool,
  onPress: PropTypes.func,

  // Requestarr: a button that's a link (to), and what its tooltip says instead of its label.
  to: PropTypes.string,
  title: PropTypes.string
};

PageToolbarButton.defaultProps = {
  spinningName: icons.SPINNER,
  isDisabled: false,
  isSpinning: false
};

export default PageToolbarButton;
