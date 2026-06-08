import type { AnyRecord } from "@/types";
import { getShipmentById, getShipments } from "@/services/fleetDomainApi";

export { getShipmentById, getShipments };

export const shipmentsApi = {
  list: () => getShipments(),
  detail: (id: string | number) => getShipmentById(id),
};
