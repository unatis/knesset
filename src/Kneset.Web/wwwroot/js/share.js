// Хелперы шеринга: копирование, системное share-меню, скролл к якорю комментария.
window.knesetShare = {
    copy: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // Фолбэк для http/старых браузеров
            const ta = document.createElement("textarea");
            ta.value = text;
            ta.style.position = "fixed";
            ta.style.opacity = "0";
            document.body.appendChild(ta);
            ta.select();
            const ok = document.execCommand("copy");
            document.body.removeChild(ta);
            return ok;
        }
    },

    canNativeShare: function () {
        return !!navigator.share;
    },

    nativeShare: async function (url, title, text) {
        if (!navigator.share) return false;
        try {
            await navigator.share({ url: url, title: title, text: text });
            return true;
        } catch {
            return false; // пользователь закрыл меню — не ошибка
        }
    },

    scrollToHash: function () {
        if (!window.location.hash) return;
        const el = document.getElementById(window.location.hash.substring(1));
        if (el) {
            el.scrollIntoView({ behavior: "smooth", block: "center" });
        }
    }
};
