const { contextBridge, ipcRenderer } = require('electron');

const invoke = (channel, payload) => ipcRenderer.invoke(channel, payload);
contextBridge.exposeInMainWorld('killwind', {
  processes: () => invoke('process:list'),
  attach: (processInfo) => invoke('process:attach', processInfo),
  detach: () => invoke('process:detach'),
  regions: (options) => invoke('process:regions', options),
  firstScan: (value) => invoke('scan:first', value),
  nextScan: (value) => invoke('scan:next', value),
  cancelScan: () => invoke('scan:cancel'),
  clearScan: () => invoke('scan:clear'),
  readInt32: (address) => invoke('memory:read-int32', address),
  writeInt32: (address, value) => invoke('memory:write-int32', { address, value }),
  addressList: () => invoke('address:list'),
  addAddress: (result) => invoke('address:add', result),
  updateAddress: (payload) => invoke('address:update', payload),
  removeAddress: (id) => invoke('address:remove', id),
  readAddresses: () => invoke('address:read-all'),
  freezeAddress: (payload) => invoke('address:freeze', payload),
  profileList: () => invoke('profile:list'),
  saveProfile: (profile) => invoke('profile:save', profile),
  loadProfile: (name) => invoke('profile:load', name),
  launchTestGame: () => invoke('test-game:launch'),
  quit: () => invoke('app:quit'),
  pickScreenPoint: () => invoke('screen:pick'),
  applyScreenEdit: (payload) => invoke('screen:apply', payload),
  applyScreenCandidate: (payload) => invoke('screen:apply-candidate', payload),
  on: (channel, callback) => {
    const allowed = ['scan:progress', 'process:state', 'freeze:event'];
    if (!allowed.includes(channel)) throw new Error('invalid event channel');
    const listener = (_event, payload) => callback(payload);
    ipcRenderer.on(channel, listener);
    return () => ipcRenderer.removeListener(channel, listener);
  },
});
