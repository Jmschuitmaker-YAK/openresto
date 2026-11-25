import {
  createContext,
  useContext,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { Platform } from "react-native";
import { getLocales } from "expo-localization";
import i18n from "@/i18n";
import { setActiveLocale } from "@/utils/locale";
import { StorageService } from "@/services/storage";
import { useBrand } from "@/context/BrandContext";
import {
  DEFAULT_LOCALE,
  LOCALE_DIRECTIONS,
  isSupportedLocale,
  type SupportedLocale,
} from "@/constants/locales";

const STORAGE_KEY = "openresto.locale";

/**
 * The phone's own language, off web only, and only when this app ships it.
 *
 * @see [LocaleContext.native.test.tsx](../tests/context/LocaleContext.native.test.tsx) — pins
 * that a device set to a shipped language wins over the brand default, and that a device set
 * to one this app does not ship falls through to it.
 */
function deviceLocale(): SupportedLocale | undefined {
  if (Platform.OS === "web") return undefined;
  const languageCode = getLocales()[0]?.languageCode;
  return isSupportedLocale(languageCode) ? languageCode : undefined;
}

/**
 * Public web visits (including the embed) start in the restaurant's configured language.
 * A guest's manual choice remains active within that document, but is not persisted.
 * Native apps and direct admin web visits retain their saved preference.
 */
function shouldPersistLocale(): boolean {
  return (
    Platform.OS !== "web" ||
    (typeof window !== "undefined" && /^\/admin(?:\/|$)/.test(window.location.pathname))
  );
}

function resolveLocale(brandDefaultLocale: string | undefined, persist: boolean): SupportedLocale {
  if (persist) {
    const stored = StorageService.getItem(STORAGE_KEY);
    if (isSupportedLocale(stored)) return stored;
  }
  return (
    deviceLocale() ?? (isSupportedLocale(brandDefaultLocale) ? brandDefaultLocale : DEFAULT_LOCALE)
  );
}

interface LocaleContextValue {
  locale: SupportedLocale;
  /**
   * The language switcher updates context immediately and persists the pick only on native
   * and admin web visits. It applies the language live via
   * `applyLocale` — the same i18next + `setActiveLocale` pair the initial resolution uses, so
   * switching languages re-formats dates/times immediately rather than only retranslating text.
   */
  setLocale: (locale: SupportedLocale) => void;
}

const LocaleContext = createContext<LocaleContextValue>({
  locale: DEFAULT_LOCALE,
  setLocale: () => {},
});

/**
 * Applies a locale to every consumer outside React state: i18next (drives `t()` and
 * `useTranslation()` re-renders), `utils/locale.ts` (drives every `utils/formatters.ts`
 * date/time/number formatter), and the document's `lang`/`dir` on web. Shared by the initial
 * resolution effect and the switcher's `setLocale`, so the two paths can't drift apart.
 */
function applyLocale(locale: SupportedLocale): void {
  setActiveLocale(locale);
  i18n.changeLanguage(locale);

  /* istanbul ignore else -- native has no `document`; every test runs under jsdom */
  if (typeof document !== "undefined") {
    document.documentElement.lang = locale;
    document.documentElement.dir = LOCALE_DIRECTIONS[locale];
  }
}

/**
 * Mounted under `BrandProvider`, since resolution reads `brand.defaultLocale`. Applies the
 * resolved locale to both i18next (`i18n.changeLanguage`) and `utils/locale.ts`
 * (`setActiveLocale`) — the second call is what makes every date/time/number formatter in
 * `utils/formatters.ts` follow the UI language instead of the device's.
 */
export function LocaleProvider({ children }: { children: ReactNode }) {
  const brand = useBrand();
  const [persist] = useState(shouldPersistLocale);
  const manualChoice = useRef<SupportedLocale | undefined>(undefined);
  const [locale, setLocaleState] = useState<SupportedLocale>(() =>
    resolveLocale(brand.defaultLocale, persist)
  );

  useLayoutEffect(() => {
    const resolved = manualChoice.current ?? resolveLocale(brand.defaultLocale, persist);
    setLocaleState(resolved);
    applyLocale(resolved);
  }, [brand.defaultLocale, persist]);

  const setLocale = (next: SupportedLocale) => {
    manualChoice.current = next;
    if (persist) StorageService.setItem(STORAGE_KEY, next);
    setLocaleState(next);
    applyLocale(next);
  };

  return <LocaleContext.Provider value={{ locale, setLocale }}>{children}</LocaleContext.Provider>;
}

export function useLocale(): LocaleContextValue {
  return useContext(LocaleContext);
}
