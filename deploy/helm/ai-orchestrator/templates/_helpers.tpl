{{- define "ai-orchestrator.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "ai-orchestrator.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{- define "ai-orchestrator.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "ai-orchestrator.commonLabels" -}}
helm.sh/chart: {{ include "ai-orchestrator.chart" . }}
{{ include "ai-orchestrator.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "ai-orchestrator.selectorLabels" -}}
app.kubernetes.io/name: {{ include "ai-orchestrator.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{- define "ai-orchestrator.control-plane.name" -}}
{{- printf "%s-control-plane" (include "ai-orchestrator.name" .) }}
{{- end }}

{{- define "ai-orchestrator.control-plane.fullname" -}}
{{- printf "%s-control-plane" (include "ai-orchestrator.fullname" .) }}
{{- end }}

{{- define "ai-orchestrator.control-plane.selectorLabels" -}}
{{- include "ai-orchestrator.selectorLabels" . }}
app.kubernetes.io/component: control-plane
{{- end }}

{{- define "ai-orchestrator.control-plane.labels" -}}
{{- include "ai-orchestrator.commonLabels" . }}
app.kubernetes.io/component: control-plane
{{- end }}

{{- define "ai-orchestrator.worker-gateway.name" -}}
{{- printf "%s-worker-gateway" (include "ai-orchestrator.name" .) }}
{{- end }}

{{- define "ai-orchestrator.worker-gateway.fullname" -}}
{{- printf "%s-worker-gateway" (include "ai-orchestrator.fullname" .) }}
{{- end }}

{{- define "ai-orchestrator.worker-gateway.selectorLabels" -}}
{{- include "ai-orchestrator.selectorLabels" . }}
app.kubernetes.io/component: worker-gateway
{{- end }}

{{- define "ai-orchestrator.worker-gateway.labels" -}}
{{- include "ai-orchestrator.commonLabels" . }}
app.kubernetes.io/component: worker-gateway
{{- end }}

{{- define "ai-orchestrator.mongodb.name" -}}
{{- printf "%s-mongodb" (include "ai-orchestrator.name" .) }}
{{- end }}

{{- define "ai-orchestrator.mongodb.fullname" -}}
{{- printf "%s-mongodb" (include "ai-orchestrator.fullname" .) }}
{{- end }}

{{- define "ai-orchestrator.mongodb.selectorLabels" -}}
{{- include "ai-orchestrator.selectorLabels" . }}
app.kubernetes.io/component: mongodb
{{- end }}

{{- define "ai-orchestrator.mongodb.labels" -}}
{{- include "ai-orchestrator.commonLabels" . }}
app.kubernetes.io/component: mongodb
{{- end }}

{{- define "ai-orchestrator.victoriametrics.name" -}}
{{- printf "%s-victoriametrics" (include "ai-orchestrator.name" .) }}
{{- end }}

{{- define "ai-orchestrator.victoriametrics.fullname" -}}
{{- printf "%s-victoriametrics" (include "ai-orchestrator.fullname" .) }}
{{- end }}

{{- define "ai-orchestrator.victoriametrics.selectorLabels" -}}
{{- include "ai-orchestrator.selectorLabels" . }}
app.kubernetes.io/component: victoriametrics
{{- end }}

{{- define "ai-orchestrator.victoriametrics.labels" -}}
{{- include "ai-orchestrator.commonLabels" . }}
app.kubernetes.io/component: victoriametrics
{{- end }}

{{- define "ai-orchestrator.vmui.name" -}}
{{- printf "%s-vmui" (include "ai-orchestrator.name" .) }}
{{- end }}

{{- define "ai-orchestrator.vmui.fullname" -}}
{{- printf "%s-vmui" (include "ai-orchestrator.fullname" .) }}
{{- end }}

{{- define "ai-orchestrator.vmui.selectorLabels" -}}
{{- include "ai-orchestrator.selectorLabels" . }}
app.kubernetes.io/component: vmui
{{- end }}

{{- define "ai-orchestrator.vmui.labels" -}}
{{- include "ai-orchestrator.commonLabels" . }}
app.kubernetes.io/component: vmui
{{- end }}

{{- define "ai-orchestrator.namespace" -}}
{{- default .Values.global.namespace .Release.Namespace }}
{{- end }}

{{- define "ai-orchestrator.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "ai-orchestrator.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{- define "ai-orchestrator.mongodb.connectionString" -}}
{{- if .Values.mongodb.auth.existingSecret }}
mongodb://$(MONGODB_ROOT_USER):$(MONGODB_ROOT_PASSWORD)@{{ include "ai-orchestrator.mongodb.fullname" . }}:{{ .Values.service.mongodb.port }}/{{ .Values.mongodb.auth.database }}?authSource=admin
{{- else }}
mongodb://{{ .Values.mongodb.auth.rootUser }}:{{ .Values.mongodb.auth.rootPassword }}@{{ include "ai-orchestrator.mongodb.fullname" . }}:{{ .Values.service.mongodb.port }}/{{ .Values.mongodb.auth.database }}?authSource=admin
{{- end }}
{{- end }}
