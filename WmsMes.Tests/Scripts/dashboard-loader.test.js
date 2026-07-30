"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

test("CDN failures create local scripts and initialize the dashboard exactly once after both load", async () => {
    const appendedScripts = [];
    const context = {
        document: {
            createElement(tagName) {
                assert.equal(tagName, "script");
                return {
                    removed: false,
                    remove() {
                        this.removed = true;
                    }
                };
            },
            head: {
                append(script) {
                    appendedScripts.push(script);
                }
            }
        }
    };
    context.window = context;
    vm.createContext(context);

    const loaderPath = path.resolve(
        __dirname,
        "..",
        "..",
        "wwwroot",
        "js",
        "dashboard-loader.js");
    vm.runInContext(fs.readFileSync(loaderPath, "utf8"), context, {
        filename: loaderPath
    });

    let initializationCount = 0;
    const initializeDashboard = () => {
        initializationCount += 1;
    };
    const firstStart = context.factoryDashboardLoader.start(initializeDashboard);
    const secondStart = context.factoryDashboardLoader.start(initializeDashboard);

    assert.strictEqual(secondStart, firstStart);
    assert.equal(appendedScripts.length, 2);
    assert.ok(appendedScripts.every(script => script.integrity?.startsWith("sha384-")));
    assert.ok(appendedScripts.every(script => script.crossOrigin === "anonymous"));

    const failedCdnScripts = [...appendedScripts];
    failedCdnScripts.forEach(script => script.onerror());
    await new Promise(resolve => setImmediate(resolve));

    assert.ok(failedCdnScripts.every(script => script.removed));
    assert.equal(appendedScripts.length, 4);
    const localScripts = appendedScripts.slice(2);
    assert.deepEqual(
        localScripts.map(script => script.src).sort(),
        [
            "/lib/chart.js/4.4.9/chart.umd.min.js",
            "/lib/microsoft-signalr/8.0.0/signalr.min.js"
        ]);
    assert.ok(localScripts.every(script => script.integrity === undefined));
    assert.equal(initializationCount, 0);

    localScripts[0].onload();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(initializationCount, 0);

    localScripts[1].onload();
    await firstStart;
    assert.equal(initializationCount, 1);
});
