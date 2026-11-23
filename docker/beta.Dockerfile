# The beta image: the exact image staging built and tested, plus this one layer, which says the server is beta. That is
# the one deliberate difference from what production ships (its number, and on "ship it" the minor and latest tags,
# stay on the image without it). The setting stays an override for the dev and QA stacks (docs/development.md#release-flow).
ARG BASE
FROM ${BASE}
ENV REQUESTARR_ENVIRONMENT=staging
