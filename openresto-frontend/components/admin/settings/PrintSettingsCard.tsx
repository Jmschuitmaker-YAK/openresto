import { useEffect, useRef, useState } from "react";
import { Switch, View } from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import Button from "@/components/common/Button";
import { ButtonRow } from "@/components/common/ButtonRow";
import ConfirmModal from "@/components/common/ConfirmModal";
import Input from "@/components/common/Input";
import Select from "@/components/common/Select";
import { AnimatedAccordion } from "@/components/common/AnimatedAccordion";
import { ModalCard } from "@/components/common/ModalCard";
import { usePersistedState } from "@/hooks/use-persisted-state";
import { theme } from "@/theme/theme";
import { AccordionCardHeader } from "./AccordionCardHeader";
import { useAppTheme } from "@/hooks/use-app-theme";
import { useBrand } from "@/context/BrandContext";
import { useCan } from "@/context/AuthContext";
import { LOCALE_LABELS, SUPPORTED_LOCALES, isSupportedLocale } from "@/constants/locales";
import { fetchRestaurants, type RestaurantDto } from "@/api/restaurants";
import {
  getPrintStatus,
  getPrintJobs,
  type PrintJobPage,
  type PrintJobStatus,
  printAction,
  registerPrintAgent,
  reprintBooking,
  type PrintStatus,
} from "@/api/printing";
import { styles } from "./settings.styles";

export function PrintSettingsCard() {
  const allowed = useCan("manage:printing");
  return allowed ? <PrintSettingsForm /> : null;
}

function PrintSettingsForm() {
  const { t, i18n } = useTranslation();
  const { colors, primaryColor } = useAppTheme();
  const [expanded, setExpanded] = usePersistedState("settings:printing:expanded", true);
  const [configuration, setConfiguration] = useState(false);
  const [history, setHistory] = useState<"all" | "problems" | null>(null);
  const [page, setPage] = useState<PrintJobPage | null>(null);
  const [cursors, setCursors] = useState<(number | null)[]>([null]);
  const [historyError, setHistoryError] = useState(false);
  const [historyReload, setHistoryReload] = useState(0);
  const [refreshing, setRefreshing] = useState(false);
  const requestBusy = useRef(false);
  const brand = useBrand();
  const [locations, setLocations] = useState<RestaurantDto[]>([]);
  const [location, setLocation] = useState<number>();
  const [status, setStatus] = useState<PrintStatus | null>(null);
  const [printer, setPrinter] = useState("bar");
  const [locale, setLocale] = useState(
    isSupportedLocale(brand.defaultLocale) ? brand.defaultLocale : "en"
  );
  const [token, setToken] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const [reload, setReload] = useState(0);
  const [reprintId, setReprintId] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<"register" | "revoke" | null>(null);

  useEffect(() => {
    let current = true;
    fetchRestaurants().then((rows) => {
      if (!current) return;
      setLocations(rows);
      setLocation(rows[0]?.id);
    });
    return () => {
      current = false;
    };
  }, []);

  useEffect(() => {
    if (location === undefined || busy) return;
    let current = true;
    let fetching = false;
    const refresh = async () => {
      if (fetching || requestBusy.current) return;
      fetching = true;
      try {
        const data = await getPrintStatus(location);
        if (current && !requestBusy.current) {
          setStatus(data);
          setError(false);
        }
      } catch {
        if (current && !requestBusy.current) {
          setStatus(null);
          setError(true);
        }
      } finally {
        fetching = false;
        if (current) setRefreshing(false);
      }
    };
    void refresh();
    const timer = setInterval(() => void refresh(), 15000);
    return () => {
      current = false;
      clearInterval(timer);
    };
  }, [location, reload, busy]);

  useEffect(() => {
    if (history === null || location === undefined) return;
    let current = true;
    getPrintJobs(location, cursors[cursors.length - 1], history === "problems")
      .then((data) => { if (current) setPage(data); })
      .catch(() => { if (current) setHistoryError(true); });
    return () => { current = false; };
  }, [history, location, cursors, historyReload]);

  function openHistory(mode: "all" | "problems") {
    setPage(null);
    setHistoryError(false);
    setCursors([null]);
    setHistory(mode);
  }

  async function perform(action: () => Promise<void>) {
    if (requestBusy.current) return;
    requestBusy.current = true;
    setBusy(true);
    setError(false);
    try {
      await action();
      if (location !== undefined) setStatus(await getPrintStatus(location));
    } catch {
      setError(true);
      setStatus(null);
    } finally {
      requestBusy.current = false;
      setBusy(false);
      setConfirm(null);
      setReprintId(null);
    }
  }

  const agent = status?.agent;
  const ready = location !== undefined && status !== null && !busy;
  const online = agent?.lastSeen != null && Date.now() / 1000 - agent.lastSeen < 90;
  const state = status === null ? (error ? "error" : "loading") : !agent ? "notConfigured" : online ? "online" : "offline";
  const validAlias = /^[A-Za-z0-9_-]{1,64}$/.test(printer);

  const register = () =>
    perform(async () => {
      if (location === undefined) return;
      const result = await registerPrintAgent(location, printer, locale, Boolean(agent));
      setToken(result.token);
    });
  const action = (kind: "enabled" | "revoke" | "test", enabled?: boolean) =>
    perform(async () => {
      if (location === undefined) return;
      await printAction(
        location,
        kind,
        kind === "enabled" ? { enabled: Boolean(enabled) } : { confirm: true }
      );
      if (kind === "revoke") setToken("");
    });

  const jobRow = (job: PrintJobStatus) => (
    <View key={job.id} style={[styles.tile, { borderColor: colors.border }]}>
      <View style={styles.tileCopy}>
        <ThemedText style={styles.tileTitle}>{t(`admin.settings.printing.kind.${job.kind}`)}</ThemedText>
        <ThemedText style={[styles.tileSub, { color: colors.muted }]}>
          {new Date(job.createdAt * 1000).toLocaleString(i18n.language)}
          {job.bookingRef ? ` · ${job.bookingRef}` : ""}
        </ThemedText>
        <ThemedText style={job.status === "uncertain" || job.reason ? styles.errorText : styles.statusText}>
          {t(`admin.settings.printing.status.${job.status}`)}
          {job.agentId !== agent?.agentId ? ` · ${t("admin.settings.printing.oldAgent")}` : ""}
        </ThemedText>
        {job.reason && <ThemedText style={styles.errorText}>{job.reason}</ThemedText>}
        {job.canReprint && agent && (
          <Button size="sm" variant="ghost" disabled={!ready} onPress={() => {
            setHistory(null);
            setReprintId(job.id);
          }}>{t("admin.settings.printing.reprint")}</Button>
        )}
      </View>
    </View>
  );

  return (
    <View style={[styles.secCard, { backgroundColor: colors.card, borderColor: colors.border }]} testID="print-settings">
      <AccordionCardHeader
        icon="print-outline"
        title={t("admin.settings.printing.title")}
        subtitle={`${agent ? agent.printer + " · " : ""}${t(`admin.settings.printing.${state}`)}${status?.counts.problems ? ` · ${t("admin.settings.printing.problems", { count: status.counts.problems })}` : ""}`}
        subtitleColor={state === "offline" || state === "error" || status?.counts.problems ? theme.colors.warning : undefined}
        expanded={expanded}
        onToggle={() => setExpanded((v) => !v)}
        primaryColor={primaryColor}
        mutedColor={colors.muted}
      />
      <AnimatedAccordion expanded={expanded}>
      <View style={[styles.secForm, { borderTopColor: colors.border }]}>
        <ThemedText style={[styles.fieldHint, { color: colors.muted }]}>{t("admin.settings.printing.subtitle")}</ThemedText>
        <View style={styles.field}>
          <Select
            accessibilityLabel={t("admin.settings.printing.location")}
            selectedValue={location}
            options={locations.map((row) => ({ label: row.name, value: row.id }))}
            onSelect={(value) => {
              if (busy) return;
              setStatus(null);
              setToken("");
              setError(false);
              setConfiguration(false);
              setHistory(null);
              setLocation(Number(value));
            }}
          />
        </View>
        {error && <ThemedText style={styles.errorText} role="alert">{t("admin.settings.printing.error")}</ThemedText>}
        {agent && (
          <>
            <View style={[styles.secRow, { borderTopColor: colors.border, paddingHorizontal: 0 }]}>
              <View style={styles.secRowCopy}>
                <ThemedText style={styles.secRowTitle}>{t("admin.settings.printing.automatic")}</ThemedText>
                <ThemedText style={[styles.secRowSub, { color: colors.muted }]}>{t("admin.settings.printing.enabled")}</ThemedText>
              </View>
              <Switch accessibilityLabel={t("admin.settings.printing.enabled")} value={agent.enabled} disabled={!ready} onValueChange={(value) => void action("enabled", value)} />
            </View>
            <ButtonRow align="start">
              <Button size="sm" variant="secondary" icon="print-outline" disabled={!ready} onPress={() => void action("test")}>{t("admin.settings.printing.test")}</Button>
              <Button size="sm" variant="secondary" icon="settings-outline" disabled={busy} onPress={() => {
                setPrinter(agent.printer);
                if (isSupportedLocale(agent.locale)) setLocale(agent.locale);
                setConfiguration((v) => !v);
              }}>{t("admin.settings.printing.configuration")}</Button>
            </ButtonRow>
          </>
        )}
        {status && (!agent || configuration || token !== "") && (
          <View style={[styles.editForm, { borderColor: colors.border }]}>
            <View style={styles.field}>
              <ThemedText style={styles.fieldLabel}>{t("admin.settings.printing.alias")}</ThemedText>
              <Input value={printer} onChangeText={setPrinter} autoCapitalize="none" />
            </View>
            <View style={styles.field}>
              <ThemedText style={styles.fieldLabel}>{t("admin.settings.printing.locale")}</ThemedText>
              <Select accessibilityLabel={t("admin.settings.printing.locale")} selectedValue={locale} options={SUPPORTED_LOCALES.map((value) => ({ label: LOCALE_LABELS[value], value }))} onSelect={(value) => {
                const next = String(value);
                if (isSupportedLocale(next)) setLocale(next);
              }} />
            </View>
            <ButtonRow align="start">
              <Button size="sm" disabled={!ready || !validAlias} onPress={() => (agent ? setConfirm("register") : void register())}>{t("admin.settings.printing.register")}</Button>
              {agent && <Button size="sm" variant="secondary" tone="danger" disabled={!ready} onPress={() => setConfirm("revoke")}>{t("admin.settings.printing.revoke")}</Button>}
            </ButtonRow>
            {token !== "" && <>
              <ThemedText>{t("admin.settings.printing.tokenWarning")}</ThemedText>
              <ThemedText selectable testID="print-agent-token">{token}</ThemedText>
              <Button variant="secondary" onPress={() => setToken("")}>{t("admin.settings.printing.hideToken")}</Button>
            </>}
          </View>
        )}
        <ButtonRow align="start">
          <Button variant="ghost" icon="refresh-outline" size="sm" disabled={busy || refreshing} onPress={() => {
            setRefreshing(true);
            setReload((n) => n + 1);
          }}>{t("admin.settings.printing.refresh")}</Button>
        </ButtonRow>
        {status && <>
          <ThemedText style={styles.secRowTitle}>{t("admin.settings.printing.jobs")}</ThemedText>
          <ThemedText style={[styles.fieldHint, { color: colors.muted }]}>{t("admin.settings.printing.pending", { count: status.counts.pending })}</ThemedText>
          {status.counts.problems > 0 && <Button variant="secondary" tone="warning" size="sm" onPress={() => openHistory("problems")}>{t("admin.settings.printing.problems", { count: status.counts.problems })}</Button>}
          {status.jobs.length === 0 && <ThemedText style={styles.fieldHint}>{t("admin.settings.printing.empty")}</ThemedText>}
          {status.jobs.slice(0, 3).map(jobRow)}
          <Button variant="ghost" size="sm" icon="chevron-forward" iconPosition="trailing" onPress={() => openHistory("all")}>{t("admin.settings.printing.history")}</Button>
        </>}
      </View>
      </AnimatedAccordion>
      <ModalCard visible={history !== null} title={t("admin.settings.printing.history")} onDismiss={() => setHistory(null)}>
        <ButtonRow align="start">
          <Button size="sm" variant={history === "all" ? "primary" : "secondary"} onPress={() => openHistory("all")}>{t("admin.settings.printing.all")}</Button>
          <Button size="sm" variant={history === "problems" ? "primary" : "secondary"} onPress={() => openHistory("problems")}>{t("admin.settings.printing.problemFilter")}</Button>
        </ButtonRow>
        {historyError ? <ThemedText style={styles.errorText}>{t("admin.settings.printing.error")}</ThemedText> : !page ? <ThemedText>{t("admin.settings.printing.loading")}</ThemedText> : <>
          {page.jobs.length === 0 && <ThemedText>{t("admin.settings.printing.empty")}</ThemedText>}
          {page.jobs.map(jobRow)}
          <ButtonRow align="start">
            <Button variant="secondary" size="sm" disabled={cursors.length <= 1} onPress={() => { setPage(null); setCursors((v) => v.slice(0, -1)); }}>{t("admin.settings.printing.previous")}</Button>
            <Button variant="secondary" size="sm" disabled={page.nextBefore === null} onPress={() => { setPage(null); setCursors((v) => [...v, page.nextBefore]); }}>{t("admin.settings.printing.next")}</Button>
          </ButtonRow>
        </>}
        <ButtonRow align="start">
          <Button variant="ghost" size="sm" onPress={() => { setPage(null); setHistoryError(false); setHistoryReload((v) => v + 1); }}>{t("admin.settings.printing.refresh")}</Button>
          <Button variant="secondary" size="sm" onPress={() => setHistory(null)}>{t("admin.settings.printing.close")}</Button>
        </ButtonRow>
      </ModalCard>
      <ConfirmModal visible={reprintId !== null} message={t("admin.settings.printing.reprintWarning")} loading={busy} onCancel={() => setReprintId(null)} onConfirm={() => {
        if (location !== undefined && reprintId !== null) void perform(() => reprintBooking(location, reprintId));
      }} />
      <ConfirmModal visible={confirm !== null} message={t("admin.settings.printing.replaceWarning")} destructive loading={busy} onCancel={() => setConfirm(null)} onConfirm={() => (confirm === "register" ? void register() : void action("revoke"))} />
    </View>
  );
}
