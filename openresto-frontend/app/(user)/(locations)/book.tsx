import { Redirect, useLocalSearchParams, type Href } from "expo-router";

/**
 * Public booking entry point. An explicit restaurantId keeps the legacy deep link;
 * without one, the locations screen opens the booking form immediately when
 * exactly one bookable restaurant is available.
 */
export default function BookQueryRedirect() {
  const { restaurantId } = useLocalSearchParams<{ restaurantId?: string }>();
  if (restaurantId) {
    return <Redirect href={`/(user)/locations/${restaurantId}` as Href} />;
  }
  return <Redirect href="/locations" />;
}
