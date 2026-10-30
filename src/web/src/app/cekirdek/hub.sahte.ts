import { HubConnection } from '@microsoft/signalr';

/** Elle tetiklenen sahte bağlantı: olay, yeniden bağlanma ve kopma testte üretilir. */
export function sahteBaglanti() {
  const dinleyiciler: Record<string, (o: unknown) => void> = {};
  let yenidenBaglandi = () => {};
  let koptu = () => {};
  const baglanti = {
    on: (ad: string, f: (o: unknown) => void) => (dinleyiciler[ad] = f),
    onreconnecting: () => {},
    onreconnected: (f: () => void) => (yenidenBaglandi = f),
    onclose: (f: () => void) => (koptu = f),
    start: () => Promise.resolve(),
    stop: () => Promise.resolve(),
  } as unknown as HubConnection;
  return {
    baglanti,
    olayGonder: (tur: string, id: string | null = null) => dinleyiciler['olay']({ tur, id }),
    yenidenBagla: () => yenidenBaglandi(),
    kopar: () => koptu(),
  };
}
