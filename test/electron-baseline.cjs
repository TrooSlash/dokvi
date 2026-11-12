const path = require('node:path');
const { app } = require('electron');
app.setPath('userData', path.resolve(__dirname, '../benchmark/electron-profile'));
require('../../md-reader/main.cjs');
