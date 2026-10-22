<template>
  <div class="info-tab">
    <div class="info-container">
      <p v-if="!userName" class="note">
        No user name is set, so saves and reviews won't record who made them.
        <button class="link-button" @click="emit('open-settings')">Set it in Settings</button>
      </p>

      <p v-if="!hasDocument" class="note">No document is open.</p>
      <p v-else-if="!info" class="note">File info is added to line 1 the first time this file is saved.</p>

      <template v-else>
        <h3 class="section-title">
          File Information
          <span v-if="dirty" class="dirty-mark" title="Unsaved changes - Apply or Reset">*</span>
        </h3>
        <div class="field">
          <label for="fileInfoTitle">
            Title
            <span class="setting-info" title="Also the header title of PDF exports, unless the Title PDF setting hides it. Set to the file name on first save.">ⓘ</span>
          </label>
          <input id="fileInfoTitle" v-model="draft.title" type="text" @keyup.enter="apply" />
        </div>
        <div class="field">
          <label for="fileInfoProject">Project</label>
          <input id="fileInfoProject" v-model="draft.project" type="text" placeholder="(none)" @keyup.enter="apply" />
        </div>
        <div class="actions">
          <button class="primary-button" :disabled="!dirty" @click="apply">Apply</button>
          <button class="secondary-button" :disabled="!dirty" @click="reset">Reset</button>
        </div>

        <dl class="details">
          <dt>Author</dt><dd>{{ info.author || '—' }}</dd>
          <dt>Created</dt><dd>{{ formatTimestamp(info.createdAt) }}</dd>
          <dt>Last updated</dt><dd>{{ formatTimestamp(info.lastUpdatedAt) }}</dd>
          <dt>Last updated by</dt><dd>{{ info.lastUpdatedBy || '—' }}</dd>
          <dt>Program version</dt><dd>{{ info.programVersion || '—' }}</dd>
        </dl>

        <h3 class="section-title">Review</h3>
        <dl class="details">
          <dt>Reviewed by</dt><dd>{{ info.reviewedBy || 'Not reviewed' }}</dd>
          <dt>Reviewed</dt><dd>{{ formatTimestamp(info.reviewedAt) }}</dd>
        </dl>
        <p v-if="savedSinceReview(info)" class="warning">
          ⚠ This file was changed after it was reviewed (last updated
          {{ formatTimestamp(info.lastUpdatedAt) }}{{ info.lastUpdatedBy ? ` by ${info.lastUpdatedBy}` : '' }}),
          so the review may not cover its latest changes. Consider having it re-reviewed, then
          use Set as Reviewed to record the new review.
        </p>
        <div class="actions">
          <button
            class="primary-button"
            :disabled="!userName"
            :title="userName ? `Record ${userName} as the reviewer, as of now, and save the file` : 'Set a user name in Settings first'"
            @click="markReviewed"
          >
            Set as Reviewed
          </button>
          <button v-if="info.reviewedBy || info.reviewedAt" class="secondary-button" @click="clearReview">
            Clear Review
          </button>
        </div>
      </template>

      <div class="form-row show-line-row">
        <label class="checkbox-label">
          <input type="checkbox" :checked="showLine" @change="onShowLineChange" />
          Show file info line in editor
          <span class="setting-info" title="The file info comment on line 1 is hidden by default so it isn't changed by accident. Edit it here instead.">ⓘ</span>
        </label>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, watch } from 'vue'
import { savedSinceReview } from '../../services/file-info'
import type { FileInfo } from '../../services/file-info'
import { getFileInfoDraft, setFileInfoDraft, clearFileInfoDraft, metadataDraftDiscarded } from '../metadata-drafts'
import type { FileInfoDraft } from '../metadata-drafts'

interface Props {
  info?: FileInfo | null
  hasDocument?: boolean
  /** Identifies the document `info` came from, so an unapplied draft stays with it. */
  docKey?: string
  userName?: string
  showLine?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  info: null,
  hasDocument: false,
  docKey: '',
  userName: '',
  showLine: false,
})

const emit = defineEmits<{
  'update': [patch: Partial<FileInfo>]
  'mark-reviewed': []
  'update-show-line': [enabled: boolean]
  'open-settings': []
  'draft-dirty': [payload: { docKey: string; dirty: boolean }]
}>()

const savedFields = (info: FileInfo | null): FileInfoDraft => ({ title: info?.title ?? '', project: info?.project ?? '' })
const sameFields = (a: FileInfoDraft, b: FileInfoDraft) => a.title === b.title && a.project === b.project

const draft = reactive<FileInfoDraft>({ ...(getFileInfoDraft(props.docKey) ?? savedFields(props.info)) })
const dirty = computed(() => !!props.info && !sameFields(draft, savedFields(props.info)))
let boundDocKey = props.docKey

// The host prompts before closing a document with an unapplied form.
function reportDirty(docKey: string, value: boolean) {
  if (docKey) emit('draft-dirty', { docKey, dirty: value })
}

watch(dirty, value => reportDirty(boundDocKey, value))

/** Park unapplied edits under the bound document, or drop a draft nothing changed. */
function stash() {
  if (!boundDocKey) return
  if (dirty.value) setFileInfoDraft(boundDocKey, { ...draft })
  else clearFileInfoDraft(boundDocKey)
  reportDirty(boundDocKey, dirty.value)
}

watch(() => [props.docKey, props.info] as const, ([docKey, info], [, oldInfo]) => {
  if (docKey !== boundDocKey) {
    stash()
    boundDocKey = docKey
    Object.assign(draft, getFileInfoDraft(docKey) ?? savedFields(info))
    reportDirty(docKey, dirty.value)
    return
  }
  // Unapplied edits outrank a refresh (a save stamping the comment, a review); clean fields follow it.
  if (sameFields(draft, savedFields(oldInfo))) Object.assign(draft, savedFields(info))
})

// The host drops a document's draft when its file closes.
watch(metadataDraftDiscarded, ({ docKey }) => {
  if (docKey && docKey === boundDocKey) Object.assign(draft, savedFields(props.info))
})

onBeforeUnmount(stash)

function apply() {
  if (!props.info || !dirty.value) return
  const saved = savedFields(props.info)
  const patch: Partial<FileInfo> = {}
  if (draft.title !== saved.title) patch.title = draft.title
  if (draft.project !== saved.project) patch.project = draft.project
  clearFileInfoDraft(boundDocKey)
  emit('update', patch)
}

function reset() {
  clearFileInfoDraft(boundDocKey)
  Object.assign(draft, savedFields(props.info))
}

// The host stamps the time: it saves any pending edits first, which must not postdate the review.
function markReviewed() {
  emit('mark-reviewed')
}

function clearReview() {
  emit('update', { reviewedBy: '', reviewedAt: '' })
}

function onShowLineChange(e: Event) {
  emit('update-show-line', (e.target as HTMLInputElement).checked)
}

function formatTimestamp(iso: string): string {
  if (!iso) return '—'
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString()
}
</script>

<style scoped>
.info-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.info-container {
  overflow-y: auto;
  flex: 1;
  padding: 12px;
  font-size: var(--calcpad-font-size-md);
  color: var(--vscode-foreground);
}

.section-title {
  margin: 16px 0 8px 0;
  font-size: var(--calcpad-font-size-lg);
  font-weight: 600;
}

.info-container > :first-child {
  margin-top: 0;
}

.actions + .details {
  margin-top: 16px;
}

.warning {
  margin: 0 0 8px 0;
  color: var(--vscode-editorWarning-foreground, #cca700);
  line-height: 1.5;
}

.dirty-mark {
  color: var(--vscode-editorWarning-foreground, #cca700);
  cursor: help;
}

.form-row {
  margin-bottom: 12px;
}

.show-line-row {
  margin-top: 20px;
  padding-top: 12px;
  border-top: 1px solid var(--vscode-widget-border);
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
}

.checkbox-label input[type="checkbox"] {
  margin: 0;
}

.setting-info {
  cursor: help;
  color: var(--vscode-descriptionForeground);
}

.note {
  margin: 0 0 12px 0;
  color: var(--vscode-descriptionForeground);
  line-height: 1.5;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 10px;
}

.field input {
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-size: var(--calcpad-font-size-md);
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.details {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 4px 12px;
  margin: 0 0 8px 0;
}

.details dt {
  color: var(--vscode-descriptionForeground);
}

.details dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.actions {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}

.primary-button,
.secondary-button {
  border: none;
  padding: 6px 14px;
  font-size: var(--calcpad-font-size-md);
  font-family: var(--vscode-font-family);
  cursor: pointer;
  border-radius: 2px;
}

.primary-button {
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
}

.primary-button:hover:not(:disabled) {
  background: var(--vscode-button-hoverBackground);
}

.primary-button:disabled,
.secondary-button:disabled {
  opacity: 0.5;
  cursor: default;
}

.secondary-button {
  background: var(--vscode-button-secondaryBackground);
  color: var(--vscode-button-secondaryForeground);
}

.secondary-button:hover:not(:disabled) {
  background: var(--vscode-button-secondaryHoverBackground);
}

.link-button {
  background: none;
  border: none;
  padding: 0;
  color: var(--vscode-textLink-foreground);
  cursor: pointer;
  font: inherit;
  text-decoration: underline;
}
</style>
