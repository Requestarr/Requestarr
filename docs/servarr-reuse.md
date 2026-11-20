# Servarr first: the web app's reuse map

Requestarr is a Servarr application. Every control Sonarr, Radarr or Prowlarr already has is used the way Servarr builds it, never rebuilt (the owner, 2026-11-20: "we're working to build a servarr application here. we can't be reinventing the wheel"). This page maps what the web app built by hand to the Servarr part it should use, what to restore, and the order to do it in.

**How it went wrong.** On 2026-10-27 a dead-code cleanup (commit `196f981`) deleted about a hundred inherited web files as unused, the generic Servarr ones among them: the selection store, the bottom bar, the filter builder, the sort and view menus, the list-state store, virtual tables, progress bars and more. The portal pages written afterwards rebuilt each of them by hand. **Rule from now on:** generic Servarr web parts are never deleted as unused, and before writing a control, find Servarr's (Prowlarr at `cb5838d`, Sonarr's `main` branch) and use or restore it.

Scope: `Portal/**`, `Settings/Requestarr/**`, `Setup/**`. "deleted" = removed in `196f981` (restore with `git show cb5838d:frontend/src/<path>`).

## Before you start

- **No Requestarr data is in Redux.** Pages load through `usePortal`/`useRequestarrApi` into `useState`. That one gap is why filters, sort, view, columns, page size and selection were each rebuilt by hand.
- **Which Sonarr to copy.** Sonarr `develop` now uses TanStack Query, zustand and React Router 6 (`useApiQuery`, `usePagedApiQuery`, `useOptionsStore`). Our tree is Prowlarr Redux + React Router 5. Copy Prowlarr's Redux creators and **Sonarr `main`** (v4, Redux) pages; use `develop` for layout only.
- **Owner decisions** (2026-11-20, latest): Sonarr's select mode (a Select button in the toolbar shows the boxes; Select All and Unselect All; the bulk actions in the bar along the bottom), filters in the toolbar's Filter menu with custom filters, a Sonarr-style footer with a colour legend and totals, Table Options for columns, and actions as toolbar buttons on the detail pages. These replace the earlier always-on checkboxes and top bar. Servarr's pager, 25 per page, with the page size in Table Options as Servarr has it.
- **Backend first for some:** portal controllers are plain `Controller`s (no SignalR); instances aren't providers; no bulk endpoints.

## Data and state

| Requestarr file | What it does | Servarr equivalent | Verdict |
|---|---|---|---|
| `Portal/usePortal.ts` | GETs `/portal/{path}` into local state, with a reload counter | A store section per list plus `Store/Actions/Creators/createFetchHandler.js`, as in `systemActions.js` | ADAPT |
| `Portal/usePortalAction.ts` | POSTs an action and tracks busy and outcome | Section thunks that set item `isX`/`xError` through `updateItem` (Sonarr main `queueActions.js`: grab/remove) | ADAPT |
| `Settings/Requestarr/requestarrApi.ts` (`callApi`) | Wraps `createAjaxRequest` in a promise | `createAjaxRequest` inside thunks | ADAPT (fold into thunks) |
| `Settings/Requestarr/useRequestarrApi.ts`, `useApiAction.ts` | Fetch, busy and error | `createFetchHandler`, section `isFetching`/`isSaving`/`saveError` | REPLACE |
| `Settings/Requestarr/useSettingsForm.ts` | Pending changes and save | `Store/Actions/Settings/general.js`: `createFetchHandler` + `createSaveHandler` + `createSetSettingValueReducer`, `createSettingsSectionSelector`, `selectSettings` | REPLACE |
| `Settings/Requestarr/useInstanceForm.ts` | Edit and test an instance | Provider stack: `createSaveProviderHandler` (exists); `createTestProviderHandler`, `createSetProviderFieldValueReducer`, `selectProviderSchema` (deleted) | ADAPT (instances as providers) |
| `Connections/useTestAll.ts` | Tests every instance | `createTestAllProvidersHandler` (deleted) | REPLACE |
| `Import/useImportJob.ts`, `AddMembers/OnboardProgress.tsx` polling | Polls on `setTimeout` | Commands: `executeCommand`, `commandActions.js`, `createCommandExecutingSelector`, SignalR command updates | ADAPT (backend Command) |
| `Portal/PortalTypes.ts` | Model types | `App/State/*AppState.ts` and models extending `App/ModelBase` (Prowlarr `Indexer/Indexer.ts`) | ADAPT |
| `Portal/useDeletesFiles.ts` | Reads one setting | A field of the settings section | ADAPT |
| `Portal/Components/useDateText.ts` | Long date text, reformatting `longDateFormat` | `Utilities/Date/formatDateTime.js`, `formatDate.js` + `createUISettingsSelector` | REPLACE |
| `Portal/portalText.ts` | Status and type words | none (domain); its `capitalised()` and six inline copies (AuditLogPage, AccessRow, ActionButton, BotStatus, InstanceField, SettingField) = `Utilities/String/firstCharToUpper.js` (deleted) | KEEP / REPLACE helper |
| Inline `{ value }: { value: boolean }` (8 places) | Change payload type | `typings/inputs.ts` `CheckInputChanged` / `InputChanged` (exists, unused) | REPLACE |

## Lists

| Requestarr file | What it does | Servarr equivalent | Verdict |
|---|---|---|---|
| `Portal/useSortedRows.ts` | Client-side sort | `createSetClientSideCollectionSortReducer` + `createClientSideCollectionSelector` with `sortPredicates` and secondary sort (deleted) | REPLACE |
| `Components/PortalFilter.tsx` (+`optionsOf`); `RequestsPage` `useRequestFilters`/`matches`/`filterOptions`; `MembersPage` `useStatusFilter`; `PickPeople` `matches` | A row of drop-downs | `Components/Menu/FilterMenu.js` (exists) + section `filters`/`filterPredicates`/`filterBuilderProps` (`optionsSelector` for type, status, instance) + `Components/Filter/**` custom filters (deleted; backend `CustomFilterController` exists). "Requested in last N days" = `filterTypes.IN_LAST` + `dateFilterPredicate` (deleted) | REPLACE |
| `Requests/requestView.ts` | View saved in `localStorage` | Section `view` + `persistState` (Sonarr main `seriesIndexActions.js`: `seriesIndex.view`) | REPLACE |
| `Components/columnsOf.ts`, inline `Column[]` (RequestsTable, MembersPage `columnsFor`) | Fixed columns | Section `columns` + `TableOptionsModalWrapper` (exists) + `createSetTableOptionReducer`, persisted | REPLACE |
| `Components/RequestsTable.tsx` | Requests table, with posters | Sonarr main `Series/Index/Table/SeriesIndexTable(+Row,+Header,+TableOptions)` (react-window); Prowlarr `Indexer/Index/Table/IndexerIndexTable.tsx` | ADAPT |
| `Members/MembersPage.tsx`, `AddMembers/PickPeople.tsx` | Client list with filter, sort, page and select | Prowlarr `Indexer/Index/IndexerIndex.tsx` + `indexerIndexActions.js` (`membersIndex` section) | ADAPT |
| `Audit/AuditLogPage.tsx` | Server-paged list, typed filters, Search button | `createServerSideCollectionHandlers` + `TablePager`, as in `System/Events/LogsTable(+Connector).js` and Prowlarr `History/History.js` + `historyActions.js` (deleted) | ADAPT |
| `Queue/QueuePage.tsx`, `AccessRequests/AccessRequestsPage.tsx`, `Outages/OutagesPage.tsx` (history) | Queue-like lists with row actions | Sonarr main `Activity/Queue`, `Activity/Blocklist` (server-side collection, row actions, select) | ADAPT |
| `RemovalsTable`, `TimelineTable`, `FollowersTable`, `Queue/TitleRemovals`, `Import/ImportPreview` | Domain tables on `Components/Table/*` | (already Servarr tables) | KEEP; take their columns from section state |

## Selection

| Requestarr file | What it does | Servarr equivalent | Verdict |
|---|---|---|---|
| `Selection/useSelection.ts` | A `Set` of picks, a page box, all matching | `App/SelectContext.tsx` + `Helpers/Hooks/useSelectState.tsx` + `Utilities/Table/*` (deleted; shift-click ranges included). Discord string IDs: generalise the ID type as Sonarr `develop` `App/Select/SelectContext.tsx` does. `selectAll` over the filtered items = "select all matching" | REPLACE |
| `Selection/RowCheckbox.tsx`, `RequestRows.RowSelect` | A row's box | `Table/Cells/TableSelectCell.js`, `VirtualTableSelectCell.js` (deleted); posters and overview: Sonarr main `Series/Index/Select/SeriesIndexPosterSelect.tsx` | REPLACE |
| `Selection/PageCheckbox.tsx` | Picks the page | `TableSelectAllHeaderCell.js` (exists); Sonarr `SeriesIndexSelectAllButton.tsx` | REPLACE |
| `Selection/SelectionBar.tsx` | Sticky bar: count, actions, clear | Prowlarr `Indexer/Index/Select/IndexerIndexSelectFooter.tsx` + `Page/PageContentFooter.js` (deleted) | REPLACE: the footer in select mode, reading `useSelect`, with `SpinnerButton` |
| `Selection/runEach.ts`, `Requests/runBulk.ts`, `useRequestBulk.tsx`, `MembersPage` `useMemberBulk` | One call per item, in turn | `createBulkEditItemHandler`, `createBulkRemoveItemHandler` (deleted) + backend bulk endpoints (`ProviderBulkResource` pattern) | ADAPT |
| `Requests/BulkActionModal.tsx` | Confirmation that lists the titles | Prowlarr `Indexer/Index/Select/Delete/DeleteIndexerModalContent.tsx` | ADAPT |

## Paging

| Requestarr file | What it does | Servarr equivalent | Verdict |
|---|---|---|---|
| `Paging/PortalPager.tsx` (`usePageSize`) | `TablePager` + its own page-size drop-down in `localStorage` | `TablePager` (exists) + `pageSize` in `TableOptionsModal` (exists, `hasPageSize`), persisted like `system.logs.pageSize` | REPLACE (page size under Options, as Servarr has it) |
| `Paging/usePagedPortal.ts` | Server paging and sort | `createServerSideCollectionHandlers`, `createSetServerSideCollection{Page,Sort,Filter}Handler` (exist), `usePaging.ts` (deleted), `PagedAppSectionState` (exists) | REPLACE |
| `Paging/useKeyedPage.ts` | Back to page 1 when sort or filter changes | Already done by the server-side sort and filter handlers | REPLACE |
| `Paging/usePagedRows.ts` | Pages the Requests and Members lists client-side | Sonarr's index doesn't page; it virtualises with react-window + `useMeasure` (deleted) | REPLACE (virtualise) |
| `Paging/PagedSection.tsx` | FieldSet + a paged list | One server-side section per detail list | ADAPT |

## Toolbars and menus

| Requestarr file | What it does | Servarr equivalent | Verdict |
|---|---|---|---|
| `Requests/RequestsToolbar.tsx` | View and Sort menus built from `SelectedMenuItem` (no sort-direction arrow) | `Menu/ViewMenu(+Item).js`, `SortMenu(+Item).js` (deleted), `FilterMenu`, `TableOptionsModalWrapper`; Sonarr main `Series/Index/Menus/SeriesIndex{View,Sort,Filter}Menu.tsx` | REPLACE |
| `Components/PortalToolbar.tsx` | One-button toolbar | `PageToolbar` + `PageToolbarButton` inline | REPLACE |
| `Settings/Requestarr/Connections/ConnectionsToolbar.tsx`, the toolbar in `RequestarrSettingsPage.tsx` | Advanced, Save, Test all | `Settings/SettingsToolbarConnector.js` (exists: advanced, save, pending-changes prompt, `additionalButtons`) | REPLACE |
| `RequestRows.RowActions`, `Members/MemberRowActions.tsx`, `memberActionRules.ts` | Likely action + overflow menu | Built from `Menu`/`MenuItem`; Sonarr `Activity/Queue/QueueRow` row buttons | KEEP (owner asked) |

## Page shell

| Requestarr file | What it does | Servarr equivalent | Verdict |
|---|---|---|---|
| `Components/PortalPage.tsx` | Render-prop shell: loading, 404, error | `PageContent`/`PageContentBody` + `LoadingIndicator`/`Alert`/`NotFound` (exist) in each page, as Sonarr main `Series/Details/SeriesDetailsPage` | REPLACE |
| `.empty` divs (Portal.css) | Empty lists | `Alert kind=INFO` (Sonarr History) or a `NoRequests` like `Series/NoSeries` | REPLACE |
| (missing) | Scroll position on back navigation | `withScrollPosition.tsx` + `Store/scrollPositions.ts` (deleted) | restore |
| (missing) | Jump bar when sorted by title or name | `Page/PageJumpBar(+Item).js` + `Utilities/Array/getIndexOfFirstCharacter.js` (deleted) | restore |
| Loading and error in `ConnectionSections`, `RequestarrSettingsPage` | Section states | `Page/PageSectionContent.js` (deleted) | REPLACE |

## Views

| Requestarr file | What it does | Servarr equivalent | Verdict |
|---|---|---|---|
| `Requests/RequestsOverview.tsx` | Overview rows | Sonarr main `Series/Index/Overview/SeriesIndexOverviews`, `SeriesIndexOverview`, `SeriesIndexOverviewInfo(+Row)`, `Options/SeriesIndexOverviewOptionsModal`, `selectOverviewOptions` | ADAPT |
| `Requests/RequestsPosters.tsx` | Poster grid | Sonarr main `Series/Index/Posters/SeriesIndexPosters` (react-window grid), `SeriesIndexPoster`, `SeriesIndexPosterInfo`, `Options/SeriesIndexPosterOptionsModal` | ADAPT |
| `RequestMedia.RequestPoster`, `RequestsPosters.Poster` | Lazy poster with fallback | Sonarr main `Series/SeriesPoster` + `SeriesImage` (placeholder image) | ADAPT |
| `Requests/RequestViews.css` | Hand-written copy of Sonarr's layout | Sonarr's own CSS for those components | REPLACE |
| `RequestMedia` `releaseText`, `OpenInLink` | Release dates; open in Sonarr or Radarr | none (domain) | KEEP |
| `Components/FollowerStack.tsx` | Follower pictures, `title` for hover | Domain; hover → `Tooltip/Popover.js` (deleted); badge → `Label` | KEEP + ADAPT |
| `Avatar.tsx`, `Person.tsx`, `MemberLink.tsx` | Discord identity | none (domain); @handle hover → `Tooltip` | KEEP |
| Progress text (`portalText` progress, seasons there vs asked, onboarding run) | Text only | `Components/ProgressBar.js` (deleted), like Sonarr `Series/Index/ProgressBar` | ADAPT |

## Detail pages

| Requestarr file | What it does | Servarr equivalent | Verdict |
|---|---|---|---|
| `Requests/RequestDetailPage.tsx` | FieldSet + DescriptionList summary; actions in a FieldSet | Sonarr main `Series/Details/SeriesDetails.js`: backdrop header, `SeriesPoster`, title, `Label size=LARGE` chips with icons, overview, links in a `Popover`; actions as `PageToolbarButton`s | ADAPT |
| `RequestDetailPage` `TitleLinks` | TMDb, TVDB, Open in | `Series/Details/SeriesDetailsLinks.tsx` | ADAPT |
| `Requests/RequestActions.tsx`, `useRequestActionRunner.tsx` | Action buttons and dialogs | Toolbar buttons + section thunks | ADAPT |
| `Members/MemberDetailPage.tsx`, `MemberActions.tsx`, `AccessRow.tsx` | Member summary and inline forms | Same header pattern (avatar in place of the poster); access editing as an edit modal (Sonarr `Series/Edit/EditSeriesModalContent`) | ADAPT |
| `TimelineTable` on the detail pages | History | Sonarr `Series/History/SeriesHistoryModal` + `Activity/History/Details/HistoryDetailsModal`; Prowlarr `History/Details/*` (deleted) | ADAPT |

## Modals and labels

| Requestarr file | What it does | Servarr equivalent | Verdict |
|---|---|---|---|
| `Components/ActionModal.tsx` | Modal + Cancel/Confirm around children | `Modal/ConfirmModal.js` (message takes a node; PRIMARY by default) | REPLACE |
| `Components/TextPromptModal.tsx` | Asks for a reason | `ConfirmModal` with a `FormInputGroup` message | REPLACE |
| `Components/ActionOutcomeAlert.tsx` | Page-level result | `appActions.showMessage` → `Page/Sidebar/Messages` (exists); errors in modals via `SpinnerErrorButton` | ADAPT |
| `ForceRemovalConfirm`, `RemovalModal`, `RemovalStart`, `RemovalScope`, `Search/LibraryRemovalModal` | Removal flow | Already `ConfirmModal`/`Modal`; shaped like Sonarr `Series/Delete/DeleteSeriesModalContent` | KEEP |
| `Members/ChangeAccessModal.tsx` | Bulk access, "leave as it is" | Prowlarr `Indexer/Index/Select/Edit/EditIndexerModalContent.tsx` (a "No change" choice per field) | ADAPT |
| `RequestActionDialogs.tsx`, `ApproveAccessModal.tsx` | Domain forms in modals | Modal + Form | KEEP |
| `Requests/ActionButton.tsx` | `SpinnerButton` + action words | (thin) | KEEP |
| Status shown as plain text in cells and summaries | Words only | `Label` kinds (Sonarr `SeriesStatusCell`, `getSeriesStatusDetails`); `InfoLabel.js` (deleted) for header chips | ADAPT |

## Settings and other

| Requestarr file | What it does | Servarr equivalent | Verdict |
|---|---|---|---|
| `RequestarrSettingsPage.tsx`, `SettingsSections.tsx`, `SettingField.tsx`, `settingsPages.ts`, `RequestSettings.tsx` | Generic settings form | `Settings/General/GeneralSettings.js` + Connector: FieldSet, `FormGroup`, `FormInputGroup {...settings.x}` (field errors and warnings from `selectSettings`) | ADAPT (the field lists stay domain) |
| `readOnlyProps.ts` | Locks provisioned fields | Duplicates `Settings/provisionedSetting.js` | REPLACE |
| `Connections/ConnectionsSettings.tsx`, `ConnectionSections.tsx`, `ConnectionCard.tsx`, `AddConnectionModal.tsx`, `SettingsConnectionModal.tsx`, `ConnectionModalFooter.tsx`, `InstanceModal.tsx`, `InstanceFields.tsx`, `InstanceField.tsx`, `InstanceAnimeFields.tsx`, `connectionLabels.ts` | Cards, add and edit modals, test | Prowlarr `Settings/Applications/**` (deleted: the Sonarr and Radarr URL + key apps, the closest match), Sonarr main `Settings/Indexers` (`Indexer.tsx` Card+Label, `AddIndexerItem`, `EditIndexerModalContent` footer); profile and root-folder choices through `providerOptionActions.js` (exists) | ADAPT (backend providers) |
| `BotStatus.tsx`, `SettingsChanges.tsx`, `Import/*` (`useChannelChecks`, `ImportChannels`, `ImportPreview`, `ImportAccessNotes`, `importTypes`) | Discord bot, change history, Requestrr import | none | KEEP |
| `Setup/*` (`SetupPage`, `SetupStep`, `DiscordStep`, `InstanceStep`, `HomeRedirect`) | First-run setup | none in Prowlarr | KEEP; `InstanceStep` should reuse the instance edit-modal content |
| `Search/PortalSearch.tsx`, `PortalSearchResults.tsx`, `usePortalSearch.ts` | Header search; `useFocusShortcut` | Prowlarr `Page/Header/IndexerSearchInput(+Result).js` (deleted, react-autosuggest), Sonarr main `Components/Page/Header/SeriesSearchInput`; `FOCUS_SEARCH_BOX` in `keyboardShortcuts.js` (exists) | ADAPT |
| `WhatsNew/WhatsNewPage.tsx` | Changelog page, its own bold-lead parsing | `System/Updates/Updates.tsx` + `UpdateChanges.tsx` + `Markdown/InlineMarkdown.js` (exist) | ADAPT |
| `OutagesPage` "now" section | Instances that are down now | `System/Status/Health` + backend health checks | ADAPT |
| `AddMembers/AddMembersPage.tsx`, `SetUpPeople.tsx`, `accessLevelChoices.ts`, `parseSeasons.ts`, `HeaderLogo.tsx` | Domain flows and helpers | none | KEEP |

## Restore list

```
App/SelectContext.tsx  App/State/ClientSideCollectionAppState.ts
Helpers/Hooks/useSelectState.tsx  Helpers/Hooks/useMeasure.ts  Helpers/Hooks/useCurrentPage.ts
Utilities/Table/{areAllSelected,getSelectedIds,getToggledRange,removeOldSelectedState,selectAll,toggleSelected}
Utilities/Array/getIndexOfFirstCharacter.js  Utilities/String/{firstCharToUpper,isString}.js
Utilities/Date/{dateFilterPredicate,isAfter,isBefore}.js  Utilities/Filter/getFilterValue.js
Utilities/Number/convertToBytes.js  Utilities/Object/{hasDifferentItems,hasDifferentItemsOrOrder,getRemovedItems,selectUniqueIds}.js
Utilities/State/{getNextId,selectProviderSchema}.js
Components/Filter/FilterModal.js  Components/Filter/CustomFilters/*
Components/Filter/Builder/{FilterBuilderModalContent(+Connector),FilterBuilderRow,FilterBuilderRowValue(+Connector,+Props,+Tag),BoolFilterBuilderRowValue,DateFilterBuilderRowValue}
Components/Menu/{SortMenu,SortMenuItem,ViewMenu,ViewMenuItem}.js
Components/Page/{PageContentFooter,PageJumpBar,PageJumpBarItem,PageSectionContent}.js
Components/Table/{VirtualTable,VirtualTableHeader,VirtualTableHeaderCell,VirtualTableRow,VirtualTableSelectAllHeaderCell}.js  Components/Table/usePaging.ts
Components/Table/Cells/{TableSelectCell,VirtualTableSelectCell,VirtualTableRowCell}.js
Components/{ProgressBar,InfoLabel}.js  Components/Tooltip/Popover.js  Components/withScrollPosition.tsx
Store/scrollPositions.ts
Store/Selectors/{createClientSideCollectionSelector,createSortedSectionSelector,createDeepEqualSelector,createProviderSelector,createProviderSettingsSelector,createExecutingCommandsSelector}
Store/Actions/Creators/Reducers/{createSetClientSideCollectionFilterReducer,createSetClientSideCollectionSortReducer,createSetProviderFieldValueReducer}.js
Store/Actions/Creators/{createBulkEditItemHandler,createBulkRemoveItemHandler,createTestProviderHandler,createTestAllProvidersHandler,createFetchSchemaHandler}.js
typings/{Table,callbacks,props}.ts
```
Plus the `.css` and `.css.d.ts` files that go with each.

**Trim when restoring:**
- `FilterBuilderRow.js`: drop the AppProfile, Category, HistoryEventType, Indexer, Privacy, Protocol and Tag row values (the value types were already removed). Add Requestarr ones, such as a member value modelled on `IndexerFilterBuilderRowValueConnector`, with type, status and instance through `optionsSelector`.
- `scrollPositions.ts`: change `indexerIndex` to Requestarr's index keys.
- `SelectContext` and `typings/props.ts`: change `id: number` to a generic ID for Discord IDs.
- `useCurrentPage.ts`: keep it on React Router 5.

## Stay deleted

- **Indexer/**, **Search/**, **History/**, **Settings/{Applications,DownloadClients,Indexers,Notifications,Profiles,Tags}**: Prowlarr domain (Applications and History are reference only).
- **Filter row values** AppProfile, Category, HistoryEventType, Indexer, Privacy, Protocol, Tag: indexer-specific.
- **Components/Chart/**, **CircularProgressBar**: no stats page planned.
- **TagList(+Connector)**, **TagFilterBuilderRowValueConnector**: Requestarr removed tags.
- **Form/** captcha, OAuth, indexer, app-profile, download-client, category inputs; **ProviderFieldFormGroup** (restore only if instances become schema providers).
- **Menu/SearchMenuItem**, **PageMenuButton**, **Page/Header/IndexerSearch***: Prowlarr search (reference only).
- **createBatchToggleEpisodeMonitoredHandler**, indexer and release selectors and actions, **piwikCheck**, **createSentryMiddleware**, **DownloadProtocol**, **ResolutionUtility**, **formatAge**: domain or telemetry.

## Suggested order of work

1. **Store sections** `requests`/`requestsIndex`, `members`/`membersIndex` on `createFetchHandler`, plus the client-side collection selector, reducers and `persistState` keys. Removes `usePortal`, `useSortedRows`, `requestView`, `columnsOf`; everything below needs it.
2. **Selection:** `SelectContext`, `useSelectState`, `Utilities/Table/*`, select cells; `SelectionBar` on `useSelect`. Unblocks Requests, Members, Add member.
3. **Menus and filters:** `SortMenu`, `ViewMenu`, `Components/Filter/**`, date utilities; replaces `RequestsToolbar` and `PortalFilter`.
4. **Index views:** `useMeasure`, `VirtualTable*`, `PageJumpBar`, `withScrollPosition`, `ProgressBar`; port Sonarr main Posters, Overview, Table and their options modals; drop `usePagedRows`.
5. **Server-side lists:** `createServerSideCollectionHandlers` sections + `usePaging`, page size in `TableOptionsModal`. Audit log, access requests, outages, Queue, detail sections; drops `PortalPager`, `usePagedPortal`, `useKeyedPage`, `PagedSection`.
6. **Shell and modals:** drop `PortalPage`, `PortalToolbar`, `ActionModal`, `TextPromptModal`; `showMessage`; `PageSectionContent`.
7. **Detail pages:** SeriesDetails-style header (`Label`, `InfoLabel`, `Popover`, toolbar actions); `HistoryDetailsModal`.
8. **Settings:** `settings.requestarr` on fetch/save handlers + `SettingsToolbarConnector`; then instances as providers (backend) with the provider handlers and Prowlarr's Applications UI.
9. **Backend, in parallel:** SignalR broadcasts, bulk endpoints, providers, Commands (import, onboarding), so 1, 2, 5 and 8 stay fresh without polling.

## Progress

- **Done (steps 1 to 3 for Requests, Members and Add member):** the `requests`/`requestIndex` and `members`/`memberIndex` store sections with Servarr's client-side collection selector, sort, filter and table-option reducers, persisted; Prowlarr's select mode (`SelectProvider`, the select mode and Select All buttons, `TableSelectCell`, Sonarr's round poster tick, the select footer); `SortMenu`, `ViewMenu`, `FilterMenu` with the custom filter builder; `TableOptionsModalWrapper` with the page size; Sonarr's footer legend and totals; status as `Label`s; bulk results in the sidebar's messages (`showMessage`). Removed: `usePortal` on those pages, `useSelection`, `SelectionBar`, `RowCheckbox`, `PageCheckbox`, `PortalFilter`, `PortalToolbar`, `requestView`, `usePagedRows`.
- **Done (step 4, Requests and Members):** one scroll, as Sonarr's series list, with no pager (the owner's choice, 2026-11-20): Sonarr main's Posters (`SeriesIndexPosters`, `SeriesIndexPoster`, `SeriesIndexPosterInfo`), Overview (`SeriesIndexOverviews`, `SeriesIndexOverview`, `SeriesIndexOverviewInfo`) and Table (`SeriesIndexTable`, `SeriesIndexTableHeader`, `SeriesIndexRow`) on react-window, each view's Options modal, the status bar under each poster (`SeriesIndexProgressBar`, `ProgressBar`), `SeriesPoster`'s placeholder, `PageJumpBar`, `withScrollPosition` and `useMeasure`; Members on the same virtual table (Prowlarr's `IndexerIndexTable`). Removed: the client-side pager (`useClientSidePage`) and the hand-written views (`RequestsPosters`, `RequestsOverview`, `RequestViews.css`).
- **Next:** step 5 (server-side lists for the detail sections, audit log, outages, access requests and the Queue, which still use `PortalPager`, `usePagedPortal` and `PagedSection`), then steps 6 to 9.
