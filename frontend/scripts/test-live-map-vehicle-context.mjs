import assert from 'node:assert/strict';
import { contextualMapEntity, includeContextualMapEntity, mapContextHasPosition, mapContextReady, mapVehicleId } from '../src/utils/liveMapVehicleContext.ts';

assert.equal(mapVehicleId('42'), '42');
assert.equal(mapVehicleId('9223372036854775807'), '9223372036854775807');
for (const invalid of [null, '', '0', '-1', '01', '4.2', 'vehicle-42', '9223372036854775808']) assert.equal(mapVehicleId(invalid), null);
const authorized = [{id:'map-42', vehicleId:42, lat:38, lng:-77}, {id:7, label:'42', lat:null, lng:null}];
assert.equal(contextualMapEntity(authorized, '42'), authorized[0]);
assert.equal(contextualMapEntity(authorized, '99'), null);
assert.equal(contextualMapEntity([{vehicle_id:42}], '42')?.vehicle_id, 42);
assert.equal(mapContextHasPosition(authorized[0]), true);
for (const entity of [{lat:null,lng:null},{lat:'',lng:-77},{lat:0,lng:0},{lat:91,lng:0},{lat:38,lng:181},{lat:'bad',lng:2}]) assert.equal(mapContextHasPosition(entity), false);
assert.equal(mapContextHasPosition({latitude:0,longitude:30}), true);
const visible = [authorized[1]];
assert.deepEqual(includeContextualMapEntity(visible, authorized[0]), [...visible, authorized[0]]);
assert.equal(includeContextualMapEntity(visible, null), visible);
assert.equal(includeContextualMapEntity(authorized, authorized[0]), authorized);
// Resolve a GPS-bearing summary immediately; wait for the separate initial
// position snapshot when the summary has no fix or omits the requested unit.
assert.equal(mapContextReady(authorized[0], false), true);
assert.equal(mapContextReady(authorized[1], false), false);
assert.equal(mapContextReady(null, false), false);
assert.equal(mapContextReady(authorized[1], true), true);
assert.equal(mapContextReady(null, true), true);
const freshAuthorized = [{...authorized[0], speedMph:40, lat:39}];
assert.equal(contextualMapEntity(freshAuthorized, '42')?.speedMph, 40);
assert.equal(contextualMapEntity(freshAuthorized, '42')?.lat, 39);
console.log('Vehicle map identity, scoped selection, absent position and roster cap checks passed');
