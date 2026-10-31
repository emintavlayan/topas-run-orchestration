import { useEffect, useRef, useState } from "react";
import Plotly from "plotly.js-basic-dist-min";

/// Manages Plotly updates, container resizing and disposal for one React chart.
export default function AnalysisChart({ title, data, layout, config }) {
    const container = useRef(null);
    const lifecycle = useRef(null);
    const pending = useRef(Promise.resolve());
    const [error, setError] = useState("");

    useEffect(() => {
        const node = container.current;
        const state = { active: true, ready: false };
        lifecycle.current = state;
        const observer = new ResizeObserver(() => {
            pending.current = pending.current.then(() => {
                if (state.active && state.ready && node.clientWidth > 0) {
                    return Plotly.Plots.resize(node);
                }
            }).catch(reason => {
                if (state.active) setError(String(reason.message || reason));
            });
        });
        observer.observe(node);
        return () => {
            state.active = false;
            observer.disconnect();
            pending.current = pending.current.then(() => Plotly.purge(node));
        };
    }, []);

    useEffect(() => {
        const node = container.current;
        const state = lifecycle.current;
        pending.current = pending.current.then(async () => {
            if (!state.active) return;
            const font = { ...layout.font, color: getComputedStyle(node).color };
            await Plotly.react(node, data, { ...layout, font }, config);
            state.ready = true;
            if (state.active) setError("");
        }).catch(reason => {
            if (state.active) setError(String(reason.message || reason));
        });
    }, [data, layout, config]);

    return (
        <div className="analysis-plot">
            <div ref={container} className="analysis-plot-canvas" role="img" aria-label={title} />
            {error && <p className="analysis-notice text-error" role="alert">Plot unavailable: {error}</p>}
        </div>
    );
}
