import { payloadSchemas, requestSchema, responseSchema, responseSchemas } from './contracts';
import type { Operation, Payload, Result } from './contracts';

export interface Transport {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
  removeEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}
declare global { interface Window { chrome?: { webview?: Transport } } }
export interface PlannerApi { request<O extends Operation>(operation: O, payload: Payload<O>): Promise<Result<O>> }

export class BridgeClient implements PlannerApi {
  private pending = new Map<string, { operation: Operation; resolve: (value: unknown) => void; reject: (error: Error) => void; timer: ReturnType<typeof setTimeout> }>();
  private disposed = false;
  constructor(private transport: Transport | undefined = window.chrome?.webview, private timeoutMs = 30000) {
    transport?.addEventListener('message', this.receive);
  }
  request<O extends Operation>(operation: O, payload: Payload<O>): Promise<Result<O>> {
    if (this.disposed) return Promise.reject(new Error('Окно закрыто. Откройте планировщик снова.'));
    if (!this.transport) return Promise.reject(new Error('Откройте планировщик из меню Чиха. В обычном браузере локальная база недоступна.'));
    if (this.pending.size >= 8) return Promise.reject(new Error('Дождитесь завершения текущих действий.'));
    const parsed = payloadSchemas[operation].safeParse(payload);
    if (!parsed.success) return Promise.reject(new Error('Проверьте поля задачи и дату.'));
    const requestId = crypto.randomUUID();
    const message = requestSchema.parse({ protocolVersion: 1, requestId, operation, payload: parsed.data });
    if (JSON.stringify(message).length > 65536) return Promise.reject(new Error('Текст слишком длинный. Сократите описание.'));
    return new Promise<Result<O>>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(requestId);
        reject(new Error('Нет ответа от Чиха. Результат действия неизвестен: обновите список перед повтором.'));
      }, this.timeoutMs);
      this.pending.set(requestId, { operation, resolve: value => resolve(value as Result<O>), reject, timer });
      try { this.transport!.postMessage(message); }
      catch { this.finish(requestId, new Error('Не удалось отправить запрос. Откройте окно снова.')); }
    });
  }
  private receive = (event: { data: unknown }) => {
    const envelope = responseSchema.safeParse(event.data);
    if (!envelope.success) {
      // A malformed/version-incompatible host reply cannot be trusted or correlated.
      for (const id of this.pending.keys()) this.finish(id, new Error('Несовместимый ответ приложения. Закройте и снова откройте планировщик.'));
      return;
    }
    const response = envelope.data;
    if (!response.requestId) return;
    const pending = this.pending.get(response.requestId);
    if (!pending) return; // Late, duplicate, or unsolicited reply.
    if (pending.operation !== response.operation) { this.finish(response.requestId, new Error('Операция ответа не совпадает с запросом.')); return; }
    if (!response.success) { this.finish(response.requestId, new Error(response.error!.message)); return; }
    const data = responseSchemas[pending.operation].safeParse(response.data);
    if (!data.success) { this.finish(response.requestId, new Error('Некорректные данные приложения. Обновите список.')); return; }
    clearTimeout(pending.timer); this.pending.delete(response.requestId); pending.resolve(data.data);
  };
  private finish(id: string, error: Error) {
    const pending = this.pending.get(id);
    if (pending) { clearTimeout(pending.timer); this.pending.delete(id); pending.reject(error); }
  }
  dispose() {
    if (this.disposed) return;
    this.disposed = true;
    this.transport?.removeEventListener('message', this.receive);
    for (const id of this.pending.keys()) this.finish(id, new Error('Окно закрыто.'));
  }
}
