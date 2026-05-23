window.appUserTimeZoneId = window.appUserTimeZoneId || "America/Lima";

window.appFormatUtcDateTime = function (value, options) {
    if (!value) return "-";

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return String(value);

    const timeZone = options?.timeZone || window.appUserTimeZoneId || "America/Lima";
    const includeSeconds = options?.includeSeconds ?? true;

    const parts = new Intl.DateTimeFormat("en-CA", {
        timeZone,
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        second: includeSeconds ? "2-digit" : undefined,
        hourCycle: "h23"
    }).formatToParts(date).reduce((acc, part) => {
        acc[part.type] = part.value;
        return acc;
    }, {});

    const time = includeSeconds
        ? `${parts.hour}:${parts.minute}:${parts.second}`
        : `${parts.hour}:${parts.minute}`;

    return `${parts.year}-${parts.month}-${parts.day} ${time}`;
};

window.appFormatUtcTime = function (value, options) {
    if (!value) return "-";

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return String(value);

    const timeZone = options?.timeZone || window.appUserTimeZoneId || "America/Lima";

    return new Intl.DateTimeFormat("es-PE", {
        timeZone,
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
        hourCycle: "h23"
    }).format(date);
};

window.appFormatUtcElements = function (root) {
    const container = root || document;
    container.querySelectorAll("[data-utc-datetime]").forEach((element) => {
        element.textContent = window.appFormatUtcDateTime(element.dataset.utcDatetime);
    });
};

document.addEventListener("DOMContentLoaded", () => {
    window.appFormatUtcElements();
});
