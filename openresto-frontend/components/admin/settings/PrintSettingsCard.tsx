import { useEffect, useState } from "react";
import { Switch, View } from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import Button from "@/components/common/Button";
import { ButtonRow } from "@/components/common/ButtonRow";
import ConfirmModal from "@/components/common/ConfirmModal";
import Input from "@/components/common/Input";
import Select from "@/components/common/Select";
import { useAppTheme } from "@/hooks/use-app-theme";
import { useBrand } from "@/context/BrandContext";
import { useCan } from "@/context/AuthContext";
import { LOCALE_LABELS, SUPPORTED_LOCALES, isSupportedLocale } from "@/constants/locales";
import { fetchRestaurants, type RestaurantDto } from "@/api/restaurants";
import {
  getPrintStatus,
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
  const { t } = useTranslation();
  const { colors } = useAppTheme();
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
    if (location === undefined) return;
    let current = true;
    getPrintStatus(location)
      .then((data) => {
        if (current) setStatus(data);
      })
      .catch(() => {
        if (current) setError(true);
      });
    return () => {
      current = false;
    };
  }, [location, reload]);

  async function perform(action: () => Promise<void>) {
    setBusy(true);
    setError(false);
    try {
      await action();
      if (location !== undefined) setStatus(await getPrintStatus(location));
    } catch {
      setError(true);
      setStatus(null);
    } finally {
      setBusy(false);
      setConfirm(null);
      setReprintId(null);
    }
  }

  const agent = status?.agent;
  const ready = location !== undefined && status !== null && !busy;
  const online = agent?.lastSeen != null && Date.now() / 1000 - agent.lastSeen < 90;
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

  return (
    <View
      style={[styles.secCard, { backgroundColor: colors.card, borderColor: colors.border }]}
      testID="print-settings"
    >
      <View style={[styles.secForm, { gap: 12 }]}>
        <ThemedText type="h3">{t("admin.settings.printing.title")}</ThemedText>
        <ThemedText>{t("admin.settings.printing.subtitle")}</ThemedText>
        <Select
          accessibilityLabel={t("admin.settings.printing.location")}
          selectedValue={location}
          options={locations.map((row) => ({ label: row.name, value: row.id }))}
          onSelect={(value) => {
            if (busy) return;
            setStatus(null);
            setToken("");
            setError(false);
            setLocation(Number(value));
          }}
        />
        {error && (
          <ThemedText style={{ color: colors.error }} role="alert">
            {t("admin.settings.printing.error")}
          </ThemedText>
        )}
        {agent && (
          <>
            <ThemedText>
              {agent.printer} ·{" "}
              {t(online ? "admin.settings.printing.online" : "admin.settings.printing.offline")}
            </ThemedText>
            <ThemedText>{t("admin.settings.printing.enabled")}</ThemedText>
            <Switch
              accessibilityLabel={t("admin.settings.printing.enabled")}
              value={agent.enabled}
              disabled={!ready}
              onValueChange={(value) => void action("enabled", value)}
            />
            <ButtonRow align="start">
              <Button size="sm" disabled={!ready} onPress={() => void action("test")}>
                {t("admin.settings.printing.test")}
              </Button>
              <Button
                size="sm"
                variant="secondary"
                tone="danger"
                disabled={!ready}
                onPress={() => setConfirm("revoke")}
              >
                {t("admin.settings.printing.revoke")}
              </Button>
            </ButtonRow>
          </>
        )}
        <ThemedText>{t("admin.settings.printing.alias")}</ThemedText>
        <Input value={printer} onChangeText={setPrinter} autoCapitalize="none" />
        <Select
          accessibilityLabel={t("admin.settings.printing.locale")}
          selectedValue={locale}
          options={SUPPORTED_LOCALES.map((value) => ({ label: LOCALE_LABELS[value], value }))}
          onSelect={(value) => {
            const next = String(value);
            if (isSupportedLocale(next)) setLocale(next);
          }}
        />
        <Button
          disabled={!ready || !validAlias}
          onPress={() => (agent ? setConfirm("register") : void register())}
        >
          {t("admin.settings.printing.register")}
        </Button>
        {token !== "" && (
          <>
            <ThemedText>{t("admin.settings.printing.tokenWarning")}</ThemedText>
            <ThemedText selectable testID="print-agent-token">
              {token}
            </ThemedText>
            <Button variant="secondary" onPress={() => setToken("")}>
              {t("admin.settings.printing.hideToken")}
            </Button>
          </>
        )}
        <Button
          variant="secondary"
          size="sm"
          disabled={busy}
          onPress={() => {
            setStatus(null);
            setError(false);
            setReload((n) => n + 1);
          }}
        >
          {t("admin.settings.printing.refresh")}
        </Button>
        <ThemedText>{t("admin.settings.printing.jobs")}</ThemedText>
        {status?.jobs.map((job) => (
          <View key={job.id}>
            <ThemedText>
              {t(`admin.settings.printing.kind.${job.kind}`)} ·{" "}
              {t(`admin.settings.printing.status.${job.status}`)}
              {job.agentId !== agent?.agentId ? ` · ${t("admin.settings.printing.oldAgent")}` : ""}
            </ThemedText>
            {job.bookingRef !== "" && <ThemedText>{job.bookingRef}</ThemedText>}
            <ThemedText>{new Date(job.createdAt * 1000).toLocaleString(locale)}</ThemedText>
            {job.reason && <ThemedText>{job.reason}</ThemedText>}
            {job.canReprint && agent && (
              <Button
                size="sm"
                variant="secondary"
                disabled={!ready}
                onPress={() => setReprintId(job.id)}
              >
                {t("admin.settings.printing.reprint")}
              </Button>
            )}
          </View>
        ))}
        <ConfirmModal
          visible={reprintId !== null}
          message={t("admin.settings.printing.reprintWarning")}
          loading={busy}
          onCancel={() => setReprintId(null)}
          onConfirm={() => {
            if (location !== undefined && reprintId !== null) {
              void perform(() => reprintBooking(location, reprintId));
            }
          }}
        />
        <ConfirmModal
          visible={confirm !== null}
          message={t("admin.settings.printing.replaceWarning")}
          destructive
          loading={busy}
          onCancel={() => setConfirm(null)}
          onConfirm={() => (confirm === "register" ? void register() : void action("revoke"))}
        />
      </View>
    </View>
  );
}
