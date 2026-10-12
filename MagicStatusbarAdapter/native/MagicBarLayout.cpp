#define NOMINMAX
#include <windows.h>
#include <uiautomation.h>
#include <wrl/client.h>
#include <algorithm>
#include <limits>
#include <cmath>
#include <thread>
#include <atomic>
#include <mutex>

using Microsoft::WRL::ComPtr;
struct BarLayout { double left, brandRight; int visible, controls; double queryMs; };

// Read-only UIA, native COM ownership. No managed automation tree/remote element cache.
static HRESULT ReadWithClient(HWND hwnd,IUIAutomation* client,IUIAutomationElement* element,BarLayout* output) {
    if (!output || !IsWindow(hwnd)) return E_INVALIDARG;
    *output = { std::numeric_limits<double>::infinity(), 156, 0, 0 };
    LARGE_INTEGER begin,frequency;QueryPerformanceCounter(&begin);QueryPerformanceFrequency(&frequency);
    HRESULT result = E_FAIL;
    {
        ComPtr<IUIAutomation> automation=client;
        ComPtr<IUIAutomationElement> root=element;
        ComPtr<IUIAutomationCondition> condition;
        ComPtr<IUIAutomationElementArray> elements;
        if (SUCCEEDED(automation->CreateTrueCondition(&condition)) &&
            SUCCEEDED(root->FindAll(TreeScope_Descendants, condition.Get(), &elements))) {
            RECT bar; GetWindowRect(hwnd, &bar);
            BOOL offscreen = TRUE; root->get_CurrentIsOffscreen(&offscreen);
            output->visible = IsWindowVisible(hwnd) && !offscreen;
            int count=0; elements->get_Length(&count);
            for (int i=0; i<count && i<256; i++) {
                ComPtr<IUIAutomationElement> item;
                if (FAILED(elements->GetElement(i, &item))) continue;
                RECT rect; BOOL hidden=TRUE; CONTROLTYPEID type=0; BSTR id=nullptr;
                if (FAILED(item->get_CurrentBoundingRectangle(&rect)) || rect.right<=rect.left) continue;
                item->get_CurrentIsOffscreen(&hidden);
                item->get_CurrentControlType(&type);
                item->get_CurrentAutomationId(&id);
                const bool honor = id && wcscmp(id,L"HonorButton")==0;
                SysFreeString(id);
                if (honor) {output->brandRight=std::max(output->brandRight,static_cast<double>(rect.right)); continue;}
                // Fixed mode adds AI recommendation controls on the LEFT. Classify by center, not x>200.
                if (!hidden && (type==UIA_ButtonControlTypeId || type==UIA_ListControlTypeId || type==UIA_DataItemControlTypeId) &&
                    rect.left>=bar.left && rect.right<=bar.right+1 && rect.top>=bar.top-1 && rect.bottom<=bar.bottom+1 &&
                    rect.right-rect.left<=(bar.right-bar.left)/2) {
                    const double center=(bar.left+bar.right)/2.0;
                    if(rect.right<=center)output->brandRight=std::max(output->brandRight,static_cast<double>(rect.right));
                    else if(rect.left>=center)output->left=std::min(output->left,static_cast<double>(rect.left));
                    else {output->brandRight=center;output->left=center;}
                    output->controls++;
                }
            }
            result=std::isfinite(output->left)?S_OK:E_FAIL;
        }
    } // Release each COM interface before ending this apartment.
    LARGE_INTEGER end;QueryPerformanceCounter(&end);output->queryMs=(end.QuadPart-begin.QuadPart)*1000.0/frequency.QuadPart;
    return result;
}
extern "C" __declspec(dllexport) HRESULT __cdecl ReadBarLayout(HWND hwnd,BarLayout* output) {
    const HRESULT apartment=CoInitializeEx(nullptr,COINIT_MULTITHREADED);HRESULT hr=E_FAIL;
    {ComPtr<IUIAutomation> client;ComPtr<IUIAutomationElement> root;
    if(SUCCEEDED(CoCreateInstance(CLSID_CUIAutomation,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&client)))&&SUCCEEDED(client->ElementFromHandle(hwnd,&root)))hr=ReadWithClient(hwnd,client.Get(),root.Get(),output);}
    if(SUCCEEDED(apartment))CoUninitialize();return hr;
}

using ChangeCallback=void(__stdcall*)();
struct Watch;
class Handler final : public IUIAutomationPropertyChangedEventHandler, public IUIAutomationStructureChangedEventHandler {
    std::atomic<ULONG> refs{1};
    ChangeCallback callback;
    HANDLE dirty;
public:
    Handler(ChangeCallback value,HANDLE event):callback(value),dirty(event){}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** out) override {
        if(!out)return E_POINTER;
        if(iid==IID_IUnknown||iid==__uuidof(IUIAutomationPropertyChangedEventHandler))*out=static_cast<IUIAutomationPropertyChangedEventHandler*>(this);
        else if(iid==__uuidof(IUIAutomationStructureChangedEventHandler))*out=static_cast<IUIAutomationStructureChangedEventHandler*>(this);
        else {*out=nullptr;return E_NOINTERFACE;}AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override{const auto count=--refs;if(!count)delete this;return count;}
    HRESULT STDMETHODCALLTYPE HandlePropertyChangedEvent(IUIAutomationElement*,PROPERTYID,VARIANT) override{SetEvent(dirty);callback();return S_OK;}
    HRESULT STDMETHODCALLTYPE HandleStructureChangedEvent(IUIAutomationElement*,StructureChangeType,SAFEARRAY*) override{SetEvent(dirty);callback();return S_OK;}
};
struct Watch {HANDLE stop,dirty;std::thread thread;std::mutex mutex;BarLayout layout{};HRESULT result=E_PENDING;std::atomic<bool> active{true};};
extern "C" __declspec(dllexport) Watch* __cdecl StartLayoutWatch(HWND hwnd,ChangeCallback callback) {
    if(!callback)return nullptr;
    auto watch=new Watch;watch->stop=CreateEventW(nullptr,TRUE,FALSE,nullptr);watch->dirty=CreateEventW(nullptr,FALSE,TRUE,nullptr);
    watch->thread=std::thread([=] {
        if(FAILED(CoInitializeEx(nullptr,COINIT_MULTITHREADED)))return;
        SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        {
            ComPtr<IUIAutomation> automation;ComPtr<IUIAutomationElement> root;
            auto handler=new Handler(callback,watch->dirty);
            if(SUCCEEDED(CoCreateInstance(CLSID_CUIAutomation,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&automation)))&&SUCCEEDED(automation->ElementFromHandle(hwnd,&root))) {
                PROPERTYID properties[]={UIA_BoundingRectanglePropertyId,UIA_IsOffscreenPropertyId};
                automation->AddPropertyChangedEventHandlerNativeArray(root.Get(),TreeScope_Subtree,nullptr,handler,properties,2);
                automation->AddStructureChangedEventHandler(root.Get(),TreeScope_Subtree,nullptr,handler);
                HANDLE events[]={watch->stop,watch->dirty};
                while(WaitForMultipleObjects(2,events,FALSE,10000)!=WAIT_OBJECT_0) {
                    if(!watch->active)continue;
                    BarLayout layout{};const auto hr=ReadWithClient(hwnd,automation.Get(),root.Get(),&layout);
                    {std::lock_guard lock(watch->mutex);watch->layout=layout;watch->result=hr;}
                    callback();
                }
                automation->RemovePropertyChangedEventHandler(root.Get(),handler);
                automation->RemoveStructureChangedEventHandler(root.Get(),handler);
            }
            handler->Release();
        }
        CoUninitialize();
    });
    return watch;
}
extern "C" __declspec(dllexport) void __cdecl StopLayoutWatch(Watch* watch) {
    if(!watch)return;SetEvent(watch->stop);if(watch->thread.joinable())watch->thread.join();CloseHandle(watch->stop);CloseHandle(watch->dirty);delete watch;
}
extern "C" __declspec(dllexport) HRESULT __cdecl ReadCachedLayout(Watch* watch,BarLayout* output) {
    if(!watch||!output)return E_INVALIDARG;std::lock_guard lock(watch->mutex);*output=watch->layout;return watch->result;
}
extern "C" __declspec(dllexport) void __cdecl RefreshLayout(Watch* watch){if(watch)SetEvent(watch->dirty);}
extern "C" __declspec(dllexport) void __cdecl SetWatchActive(Watch* watch,BOOL active){if(watch&&watch->active.exchange(active)!=!!active&&active)SetEvent(watch->dirty);}
